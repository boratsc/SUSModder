using System;

namespace SUSModder.Core.Services
{
    public interface IChatReadStateStore
    {
        long GetReadWatermark();
        void SetReadWatermark(long unixMs);
    }

    /// <summary>Optional chat convenience state; persistence failures must not interrupt chat.</summary>
    public sealed class UserSettingsChatReadStateStore : IChatReadStateStore
    {
        private readonly UserSettingsService _settings;

        public UserSettingsChatReadStateStore(UserSettingsService settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public long GetReadWatermark()
        {
            try { return _settings.LoadUserSettings().ChatReadWatermarkUnixMs; }
            catch { return 0; }
        }

        public void SetReadWatermark(long unixMs)
        {
            try
            {
                var settings = _settings.LoadUserSettings();
                if (settings.ChatReadWatermarkUnixMs == unixMs) return;
                settings.ChatReadWatermarkUnixMs = unixMs;
                _settings.SaveUserSettings(settings);
            }
            catch
            {
                // Best effort only; chat remains usable when settings persistence fails.
            }
        }
    }
}
