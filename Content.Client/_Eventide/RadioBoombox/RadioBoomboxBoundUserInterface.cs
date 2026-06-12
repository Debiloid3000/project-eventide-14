using Content.Shared._Eventide.RadioBoombox;
using Robust.Client.GameObjects;
using System; // Нужно для типа Enum

namespace Content.Client._Eventide.RadioBoombox
{
    public sealed class RadioBoomboxBoundUserInterface : BoundUserInterface
    {
        private RadioBoomboxWindow? _window;

        // ИСПРАВЛЕНО: Теперь тут строго Enum uiKey вместо object uiKey
        public RadioBoomboxBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

        protected override void Open()
        {
            base.Open();
            _window = new RadioBoomboxWindow();
            _window.OnClose += Close;
            
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
