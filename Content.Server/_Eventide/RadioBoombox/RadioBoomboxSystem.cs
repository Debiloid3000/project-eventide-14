using Content.Shared.Interaction;
using Content.Shared._Eventide.RadioBoombox;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Eventide.RadioBoombox
{
    public sealed class RadioBoomboxSystem : EntitySystem
    {
        [Dependency] private readonly SharedAudioSystem _audio = default!; 
        // UserInterfaceSystem больше не нужен, ActivatableUI сделает всё сам!

        public override void Initialize()
        {
            base.Initialize();
            
            // Больше никакого OnActivateInHand! За открытие UI теперь отвечает движок.
            SubscribeInterfaceMessage<RadioBoomboxComponent, RadioBoomboxUrlChangedMessage>(OnUrlChanged);
            SubscribeInterfaceMessage<RadioBoomboxComponent, RadioBoomboxTogglePlayMessage>(OnTogglePlay);
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
        }

        private void StopRadioAudio(EntityUid uid, RadioBoomboxComponent component)
        {
        }
    }
}
