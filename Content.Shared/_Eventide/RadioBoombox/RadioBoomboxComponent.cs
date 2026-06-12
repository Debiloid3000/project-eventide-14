using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Eventide.RadioBoombox
{
    [RegisterComponent, NetworkedComponent]
    public sealed partial class RadioBoomboxComponent : Component
    {
        /// <summary>
        /// Текущая URL ссылка на аудиофайл (mp3/wav)
        /// </summary>
        [ViewVariables(VVAccess.ReadWrite)]
        public string StreamUrl { get; set; } = string.Empty;

        /// <summary>
        /// Играет ли музыка в данный момент
        /// </summary>
        [ViewVariables(VVAccess.ReadWrite)]
        public bool IsPlaying { get; set; } = false;

        /// <summary>
        /// Радиус, в котором игроки будут слышать бумбокс
        /// </summary>
        [DataField("range")]
        public float Range { get; set; } = 10f;
    }

    // Ключ для открытия UI
    [Serializable, NetSerializable]
    public enum RadioBoomboxUiKey : byte
    {
        Key
    }

    // Сообщения от Клиента к Серверу
    [Serializable, NetSerializable]
    public sealed class RadioBoomboxUrlChangedMessage : BoundUserInterfaceMessage
    {
        public string Url { get; }
        public RadioBoomboxUrlChangedMessage(string url) => Url = url;
    }

    [Serializable, NetSerializable]
    public sealed class RadioBoomboxTogglePlayMessage : BoundUserInterfaceMessage
    {
    }
}
