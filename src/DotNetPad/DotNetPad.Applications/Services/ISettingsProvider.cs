namespace Waf.DotNetPad.Applications.Services
{
    public interface ISettingsProvider
    {
        T LoadSettings<T>(string fileName) where T : class, new();

        void SaveSettings(string fileName, object settings);
    }
}
