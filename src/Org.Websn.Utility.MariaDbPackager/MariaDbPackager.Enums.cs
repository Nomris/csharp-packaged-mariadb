namespace Org.Websn.Utility
{
    public static partial class MariaDbPackager
    {
        public enum ReleaseChannel : byte
        {
            Stable = 1,
            Canidate = 2,
            Alpha = 3
        }

        public enum SupportClass : byte
        {
            LongTermSupport = 1,
            ShortTermSupport = 2,
        }

        internal enum InstallationMode : byte
        {
            Automatic = 1,
            Manuel = 2,
        }
    }
}
