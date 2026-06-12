using Content.Shared.RadioBoombox;
using Robust.Client.GameObjects;

namespace Content.Client._Eventide.RadioBoombox
{
    public sealed class RadioBoomboxBoundUserInterface : BoundUserInterface
    {
        private RadioBoomboxWindow? _window;

        public RadioBoomboxBoundUserInterface(EntityUid owner, object uiKey) : base(owner, uiKey) { }

        protected override void Open()
        {
            base.Open();
            _window = new RadioBoomboxWindow();
            _window.OnClose += Close;

            // Передаем клики из окна на сервер
            _window.OnPlayPressed += () => SendMessage(new RadioBoomboxTogglePlayMessage());
            _window.OnUrlConfirmed += (url) => SendMessage(new RadioBoomboxUrlChangedMessage(url));

            _window.OpenToLeft();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _window?.Close();
        }
    }
}
