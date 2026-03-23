namespace AppPlatform.Abstractions;

public interface IProductShellModule
{
    IReadOnlyList<ShellNavigationItemDefinition> GetPrimaryNavigation();

    IReadOnlyList<SettingsSectionDefinition> GetSettingsSections();

    AboutContentDefinition GetAboutContent();

    IReadOnlyList<SupportActionDefinition> GetSupportActions();
}
