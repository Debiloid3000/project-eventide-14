using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Eventide.RadioBoombox // Проверь эту строку!
{
    [RegisterComponent, NetworkedComponent]
    public sealed partial class RadioBoomboxComponent : Component
    {
        [ViewVariables(VVAccess.ReadWrite)]
        public string StreamUrl { get; set; } = string.Empty;

        [ViewVariables(VVAccess.ReadWrite)]
        public bool IsPlaying { get; set; } = false;

        [DataField("range")]
        public float Range { get; set; } = 10f;
    }

    [Serializable, NetSerializable]
    public enum RadioBoomboxUiKey : byte
    {
        Key
    }

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
