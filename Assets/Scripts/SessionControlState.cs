namespace RealtimePatient
{
    /// <summary>Conversation controls, independent of Unity's world clock.</summary>
    public sealed class SessionControlState
    {
        public bool Paused { get; private set; }
        public bool NavigationPromptOpen { get; private set; }
        public bool CanAcceptSpeech(bool active, bool ready) => active && ready && !Paused && !NavigationPromptOpen;
        public bool CanPause(bool active, bool ready, bool voiceBusy) => active && ready && !voiceBusy && !Paused && !NavigationPromptOpen;
        public bool Pause(bool active, bool ready, bool voiceBusy)
        {
            if (!CanPause(active, ready, voiceBusy)) return false;
            Paused = true; return true;
        }
        public bool Resume(bool active, bool ready)
        {
            if (!Paused || !active || !ready || NavigationPromptOpen) return false;
            Paused = false; return true;
        }
        public void SetNavigationPrompt(bool open) => NavigationPromptOpen = open;
        public void Reset() { Paused = NavigationPromptOpen = false; }
    }
}
