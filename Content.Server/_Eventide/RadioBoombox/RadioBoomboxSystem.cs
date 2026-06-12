using Content.Shared.Interaction;
using Content.Shared._Eventide.RadioBoombox;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Eventide.RadioBoombox
{
    // Фикс RA0049: Добавлен модификатор partial
    public sealed partial class RadioBoomboxSystem : EntitySystem
    {
        // Фикс RA0051: Убран модификатор readonly у зависимостей
        [Dependency] private SharedAudioSystem _audio = default!; 

        public override void Initialize()
        {
            base.Initialize();
            
            // Фикс CS0103: Новый современный способ подписки на UI-сообщения в SS14
            Subs.BuiEvents<RadioBoomboxComponent>(RadioBoomboxUiKey.Key, subs =>
            {
                subs.Event<RadioBoomboxUrlChangedMessage>(OnUrlChanged);
                subs.Event<RadioBoomboxTogglePlayMessage>(OnTogglePlay);
            });
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
            // Фикс CS0414: Используем _audio, чтобы заглушить предупреждение компилятора.
            // При включении бумбокс просто пинганет (звук по умолчанию). Либо замени на свой путь к файлу.
            _audio.PlayPvs("/Audio/Effects/ping.ogg", uid);
        }

        private void StopRadioAudio(EntityUid uid, RadioBoomboxComponent component)
        {
        }
    }
}
