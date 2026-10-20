using System.Linq;
using System.Text.RegularExpressions;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Systems;
using Content.Server.Power.Components;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Radio.EntitySystems;
using Content.Shared.Roles;
using Content.Shared.StatusIcon;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Silicons.StationAi;
using Content.Shared.Speech;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Replays;
using Robust.Shared.Utility;

namespace Content.Server.Radio.EntitySystems;

/// <inheritdoc/>
public sealed partial class RadioSystem : SharedRadioSystem
{
    [Dependency] private INetManager _netMan = default!;
    [Dependency] private IReplayRecordingManager _replay = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private AccessReaderSystem _accessReader = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private EntityQuery<TelecomExemptComponent> _exemptQuery = default!;

    private readonly Dictionary<string, string[]> _departments = new()
    {
        { "fcdf03", ["командование", "кэп", "капитан", "глава персонала"] },
        { "d98b71", ["юридический отдел", "магистрат", "юрист", "агент внутренних дел"] },
        { "1563bd", ["служба безопасности", "бриг", "варден", "смотритель", "инструктор", "детектив", "пилот сб", "бригмед", "кадет"] },
        { "57b8f0", ["медицинский отдел", "главный врач", "ведущий врач", "химик", "врач", "парамед", "коронер", "психолог", "интерн"] },
        { "c68cfa", ["научный отдел", "рнд", "нио", "научный руководитель", "ведущий учёный", "учёный", "робоёб", "лаборант", "анома"] },
        { "f2ac26", ["инженерный отдел", "инженерный", "старший инженер", "ведущий инженер", "атмосферный техник", "атмос", "инженер", "инженер стажёр"] },
        { "a46106", ["отдел снабжения", "карго", "каргонцы", "ведущий утилизатор", "ведущий утиль", "утиль", "утилизатор", "грузчик"] },
        { "6ca729", ["сервисный отдел", "сервис", "менеджер", "шеф", "повар", "ботаник", "бармен", "боксер", "уборщик", "библиотекарь", "священик", "святой отец", "зоотехник", "репортёр", "музыкант"] },
        { "2ed2fd", ["искусственный интеллект", "юнит", "борг"] },
        { "fb77f3", ["клуня", "клоун"] },
        { "d0d0d0", ["мим"] }
    };

    // set used to prevent radio feedback loops.
    private readonly HashSet<string> _messages = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<IntrinsicRadioReceiverComponent, RadioReceiveEvent>(OnIntrinsicReceive);
        SubscribeLocalEvent<IntrinsicRadioTransmitterComponent, EntitySpokeEvent>(OnIntrinsicSpeak);
    }

    private void OnIntrinsicSpeak(EntityUid uid, IntrinsicRadioTransmitterComponent component, EntitySpokeEvent args)
    {
        if (args.Channel != null && component.Channels.Contains(args.Channel.ID))
        {
            SendRadioMessage(uid, args.Message, args.Channel, uid);
            args.Channel = null; // prevent duplicate messages from other listeners.
        }
    }

    private void OnIntrinsicReceive(EntityUid uid, IntrinsicRadioReceiverComponent component, ref RadioReceiveEvent args)
    {
        if (TryComp(uid, out ActorComponent? actor))
            _netMan.ServerSendMessage(args.ChatMsg, actor.PlayerSession.Channel);
    }

    /// <inheritdoc/>
    public override void SendRadioMessage(EntityUid messageSource, string message, RadioChannelPrototype channel, EntityUid radioSource, bool escapeMarkup = true)
    {
        // TODO if radios ever garble / modify messages, feedback-prevention needs to be handled better than this.
        if (!_messages.Add(message))
            return;

        var evt = new TransformSpeakerNameEvent(messageSource, MetaData(messageSource).EntityName);
        RaiseLocalEvent(messageSource, evt);

        var name = evt.VoiceName;
        name = _chat.ChatNameLinks ? $"[textlink=\"{FormattedMessage.EscapeStringParameter(name)}\" entity=\"{GetNetEntity(messageSource)}\" color=\"{channel.Color.ToHex()}\"]" : FormattedMessage.EscapeText(name);

        SpeechVerbPrototype speech;
        if (evt.SpeechVerb != null && ProtoMan.Resolve(evt.SpeechVerb, out var evntProto))
            speech = evntProto;
        else
            speech = _chat.GetSpeechVerb(messageSource, message);

        var content = escapeMarkup
            ? FormattedMessage.EscapeText(message)
            : message;

        var headsetColor = TryComp(radioSource, out HeadsetComponent? headset) ? headset.Color : channel.Color;
        var job = string.Empty;
        var idCard = GetIdCard(messageSource);
        var jobTitle = idCard != null && !string.IsNullOrEmpty(idCard.LocalizedJobTitle)
            ? _chat.SanitizeMessageCapital(idCard.LocalizedJobTitle)
            : Loc.GetString("chat-radio-source-unknown");
        var jobLabel = $"[{jobTitle}]";
        var jobColor = GetIdCardColor(idCard);
        var radioIcon = Loc.GetString("radio-icon-tag",
            ("path", GetIdSprite(messageSource, idCard)),
            ("scale", "3"),
            ("text", jobLabel),
            ("color", jobColor));

        // Sunrise-style radio identity: render the profession icon and a department-colored title before the speaker.
        name = $"{radioIcon} [color={jobColor}]{FormattedMessage.EscapeText(jobLabel)}[/color] {name}";

        content = Highlight(content);

        var wrappedMessage = Loc.GetString(speech.Bold ? "chat-radio-message-wrap-bold" : "chat-radio-message-wrap",
            ("channel-color", channel.Color),
            ("headset-color", headsetColor),
            ("fontType", speech.FontId),
            ("fontSize", speech.FontSize),
            ("verb", Loc.GetString(_random.Pick(speech.SpeechVerbStrings))),
            ("channel", $"\\[{channel.LocalizedName}\\]"),
            ("name", name),
            ("message", content),
            ("job", job));

        // most radios are relayed to chat, so lets parse the chat message beforehand
        var chat = new ChatMessage(
            ChatChannel.Radio,
            message,
            wrappedMessage,
            NetEntity.Invalid,
            null);
        var chatMsg = new MsgChatMessage { Message = chat };
        var ev = new RadioReceiveEvent(message, messageSource, channel, radioSource, chatMsg);

        var sendAttemptEv = new RadioSendAttemptEvent(channel, radioSource);
        RaiseLocalEvent(ref sendAttemptEv);
        RaiseLocalEvent(radioSource, ref sendAttemptEv);
        var canSend = !sendAttemptEv.Cancelled;

        var sourceMapId = Transform(radioSource).MapID;
        var hasActiveServer = HasActiveServer(sourceMapId, channel.ID);
        var sourceServerExempt = _exemptQuery.HasComp(radioSource);

        var radioQuery = EntityQueryEnumerator<ActiveRadioComponent, TransformComponent>();
        while (canSend && radioQuery.MoveNext(out var receiver, out var radio, out var transform))
        {
            if (!radio.ReceiveAllChannels)
            {
                if (!radio.Channels.Contains(channel.ID) || (TryComp<IntercomComponent>(receiver, out var intercom) &&
                                                             !intercom.SupportedChannels.Contains(channel.ID)))
                    continue;
            }

            if (!channel.LongRange && transform.MapID != sourceMapId && !radio.GlobalReceive)
                continue;

            // don't need telecom server for long range channels or handheld radios and intercoms
            var needServer = !channel.LongRange && !sourceServerExempt;
            if (needServer && !hasActiveServer)
                continue;

            // check if message can be sent to specific receiver
            var attemptEv = new RadioReceiveAttemptEvent(channel, radioSource, receiver);
            RaiseLocalEvent(ref attemptEv);
            RaiseLocalEvent(receiver, ref attemptEv);
            if (attemptEv.Cancelled)
                continue;

            // send the message
            RaiseLocalEvent(receiver, ref ev);
        }

        if (name != Name(messageSource))
            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Radio message from {ToPrettyString(messageSource):user} as {name} on {channel.LocalizedName}: {message}");
        else
            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Radio message from {ToPrettyString(messageSource):user} on {channel.LocalizedName}: {message}");

        _replay.RecordServerMessage(chat);
        _messages.Remove(message);
    }

    /// <inheritdoc cref="TelecomServerComponent"/>
    private bool HasActiveServer(MapId mapId, string channelId)
    {
        var servers = EntityQuery<TelecomServerComponent, EncryptionKeyHolderComponent, ApcPowerReceiverComponent, TransformComponent>();
        foreach (var (_, keys, power, transform) in servers)
        {
            if (transform.MapID == mapId &&
                power.Powered &&
                keys.Channels.Contains(channelId))
            {
                return true;
            }
        }
        return false;
    }


    private const string NoIdIconPath = "/Textures/Interface/Misc/job_icons.rsi/NoId.png";
    private const string BorgIconPath = "/Textures/Interface/Misc/job_icons.rsi/Borg.png";
    private const string StationAiIconPath = "/Textures/Interface/Misc/job_icons.rsi/StationAi.png";
    private const string DefaultJobColor = "#9FED58";

    private IdCardComponent? GetIdCard(EntityUid senderUid)
    {
        if (!_accessReader.FindAccessItemsInventory(senderUid, out var accessItems))
            return null;

        foreach (var item in accessItems)
        {
            if (TryComp(item, out PdaComponent? pda) &&
                pda.ContainedId is { } containedId &&
                TryComp(containedId, out IdCardComponent? containedIdCard))
            {
                return containedIdCard;
            }

            if (TryComp(item, out IdCardComponent? idCard))
                return idCard;
        }

        return null;
    }

    private string GetIdSprite(EntityUid senderUid, IdCardComponent? idCard)
    {
        // Match Sunrise: silicons use dedicated job icons even when they do not carry an ID card.
        if (HasComp<BorgChassisComponent>(senderUid))
            return BorgIconPath;

        if (HasComp<StationAiHeldComponent>(senderUid))
            return StationAiIconPath;

        if (idCard is null || !_prototypeManager.TryIndex(idCard.JobIcon, out var iconPrototype))
            return NoIdIconPath;

        return iconPrototype.Icon switch
        {
            SpriteSpecifier.Texture texture => texture.TexturePath.CanonPath,
            SpriteSpecifier.Rsi rsi => $"{rsi.RsiPath.CanonPath}/{rsi.RsiState}.png",
            _ => NoIdIconPath,
        };
    }

    private string GetIdCardColor(IdCardComponent? idCard)
    {
        if (idCard is null)
            return DefaultJobColor;

        DepartmentPrototype? selected = null;

        foreach (var departmentId in idCard.JobDepartments)
        {
            if (!_prototypeManager.TryIndex(departmentId, out var department))
                continue;

            if (selected is null || IsPreferredDepartment(department, selected))
                selected = department;
        }

        // Fallback for ID cards which have a job prototype but no stored department list.
        if (selected is null &&
            idCard.JobPrototype is { } jobId &&
            _prototypeManager.TryIndex(jobId, out var job))
        {
            foreach (var department in _prototypeManager.EnumeratePrototypes<DepartmentPrototype>())
            {
                if (!department.Roles.Contains(job.ID))
                    continue;

                if (selected is null || IsPreferredDepartment(department, selected))
                    selected = department;
            }
        }

        return selected?.Color.ToHex() ?? DefaultJobColor;
    }

    private static bool IsPreferredDepartment(DepartmentPrototype candidate, DepartmentPrototype current)
    {
        if (candidate.Primary != current.Primary)
            return candidate.Primary;

        return candidate.Weight > current.Weight;
    }

    private string Highlight(string message)
    {
        foreach (var (color, words) in _departments)
        {
            foreach (var word in words)
            {
                var pattern = BuildDepartmentWordPattern(word);
                var regex = new Regex($@"\w*{pattern}\w*", RegexOptions.IgnoreCase);
                message = regex.Replace(message, match => $"[color=#{color}]{match.Value}[/color]");
            }
        }

        return message;
    }

    private static string BuildDepartmentWordPattern(string word)
    {
        var pattern = string.Empty;
        foreach (var letter in word)
        {
            var letterPattern = letter switch
            {
                'л' => "[лв]",
                'р' => "[рв]",
                'ы' => "[иы]",
                _ => Regex.Escape(letter.ToString())
            };

            pattern += letterPattern + "+";
        }

        return pattern;
    }
}
