using Content.Shared.Interaction;
using Content.Shared.RadioBoombox;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Player;

namespace Content.Server._Eventide.RadioBoombox
{
    public sealed class RadioBoomboxSystem : EntitySystem
    {
        [Dependency] lipsisAudioSystem _audio = default!; // В оригинале: SharedAudioSystem или AudioSystem
        [Dependency] private readonly UserInterfaceSystem _ui = default!;

        public override void Initialize()
        {
            base.Initialize();
            
            SubscribeLocalEvent<RadioBoomboxComponent, UseInHandEvent>(OnUseInHand);
            
            // Подписка на действия из UI
            SubscribeInterfaceMessage<RadioBoomboxComponent, RadioBoomboxUrlChangedMessage>(OnUrlChanged);
            SubscribeInterfaceMessage<RadioBoomboxComponent, RadioBoomboxTogglePlayMessage>(OnTogglePlay);
        }

        private void OnUseInHand(EntityUid uid, RadioBoomboxComponent component, UseInHandEvent args)
        {
            // Открываем UI при прожатии предмета в руке
            if (_ui.TryOpenUi(uid, RadioBoomboxUiKey.Key, args.User))
                args.Handled = true;
        }

        private void OnUrlChanged(EntityUid uid, RadioBoomboxComponent component, RadioBoomboxUrlChangedMessage args)
        {
            // Валидация ссылки (Вариант 1)
            // В идеале здесь должна быть проверка: начинается ли с http/https и заканчивается ли на mp3/wav
            if (string.IsNullOrWhiteSpace(args.Url) || !args.Url.StartsWith("http"))
                return;

            component.StreamUrl = args.Url;
            
            // Если музыка играла — перезапускаем с новым треком
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
            // В RobustToolbox для стриминга внешних аудио обычно используется кастомный AudioParams
            // или интеграция с библиотеками типа воспроизведения потока.
            // Ниже — концептуальный запуск эмбиент-звука на позиции объекта
            
            var audioParams = AudioParams.Default.WithVolume(-5f).WithMaxDistance(component.Range);
            
            // ПРИМЕЧАНИЕ: Нативный Robust требует, чтобы файлы были в Resources сервера. 
            // Для полноценного стриминга URL (Вариант 1) ваш форк должен поддерживать AudioSystem.PlayFromUrl()
            // _audio.PlayFromUrl(component.StreamUrl, Filter.Pvs(uid), uid, audioParams);
        }

        private void StopRadioAudio(EntityUid uid, RadioBoomboxComponent component)
        {
            // Логика остановки текущего аудио-сигнала (сброс аудио-потока для сущности)
        }
    }
}
