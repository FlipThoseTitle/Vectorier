namespace Vectorier.Services.Tutorial;

/// <summary>
/// Counters of things the user did, for the tutorial to wait on (a drag with a tool finished, a snap onto a
/// platform, a save, a game launch). They only ever grow; the tutorial compares them with their value when a step
/// starts.
/// </summary>
public static class TutorialSignals
{
    public static int MoveDrags;
    public static int RotateDrags;
    public static int ScaleDrags;
    public static int RectDrags;
    public static int PlatformSnapDrags;
    public static int Saves;
    public static int GameLaunches;
}
