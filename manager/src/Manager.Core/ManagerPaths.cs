namespace ScheduleI.Manager;

public static class ManagerPaths
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
#if MAINTAINER
        "ScheduleIModManager.Maintainer"
#else
        "ScheduleIModManager"
#endif
    );
}
