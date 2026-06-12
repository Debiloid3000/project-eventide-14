using Content.Shared.Interaction;
using Content.Shared._Eventide.RadioBoombox; // Исправленный namespace
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems; // Системы звуков лежат тут

namespace Content.Server._Eventide.RadioBoombox
{
    public sealed class RadioBoomboxSystem : EntitySystem
    {
        // Используем стандартную общую аудиосистему движка Robust
        [Dependency] private readonly SharedAudioSystem _audio = default!; 
        [Dependency] private readonly UserInterfaceSystem _ui = default!;

        public override void Initialize()
        {
            base.Initialize();
            
            SubscribeLocalEvent<RadioBoomboxComponent, UseInHandEvent>(OnUseInHand);
            SubscribeInterfaceMessage<RadioBoomboxComponent, RadioBoomboxUrlChangedMessage>(OnUrlChanged);
            SubscribeInterfaceMessage<RadioBoomboxComponent, RadioBoomboxTogglePlayMessage>(OnTogglePlay);
        }

        private void OnUseInHand(EntityUid uid, RadioBoomboxComponent component, UseInHandEvent args)
        {
            if (_ui.TryOpenUi(uid, RadioBoomboxUiKey.Key, args.User))
                args.Handled = true;
        }

        private void OnUrlChanged(EntityUid uid, RadioBoomboxComponent component, RadioBoomboxUrlChangedMessage args)
        {
            if (string.IsNullOrWhiteSpace(args.Url) || !args.Url.StartsWith("http"))
                return;

            component.StreamUrl = args.Url;
            
            if (component.IsPlaying)
            {
                StopRadioAudio(uid, component);
                StartRadioAudio(uid, component);
            }
        }

        private void OnTogglePlay(EntityUid uid, RadioBoomboxComponent component, RadioBoomboxTogglePlayMessage args)
        {
            if (string.IsNullOrEmpty(component.StreamUrl))
                return;

            component.IsPlaying = !component.IsPlaying;

            if (component.IsPlaying)
                StartRadioAudio(uid, component);
            else
                StopRadioAudio(uid, component);
        }

        private void StartRadioAudio(EntityUid uid, RadioBoomboxComponent component)
        {
            // Базовый запуск эмбиент-звука (для локальных ресурсов)
            // Реализация полноценного стриминга из сети зависит от кастомных либ вашего форка
        }

        private void StopRadioAudio(EntityUid uid, RadioBoomboxComponent component)
        {
            // Остановка аудио-потока
        }
    }
}
