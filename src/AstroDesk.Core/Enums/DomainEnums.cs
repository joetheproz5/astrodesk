namespace AstroDesk.Core.Enums;

public enum SessionType
{
    MilkyWay,
    Starscape,
    StarTrails,
    Moon,
    Planet,
    Constellation,
    DeepSky,
    Timelapse,
    TestSession,
    Other
}

public enum SessionStatus
{
    Planned,
    Active,
    Paused,
    Completed
}

public enum DewRisk
{
    Unavailable,
    Low,
    Moderate,
    High
}

public enum ObservingQuality
{
    Unavailable,
    Poor,
    Fair,
    Good,
    Excellent
}

public enum SessionNoteKind
{
    General,
    Observation,
    Problem
}

public enum NightDisplayMode
{
    NormalDark,
    Dim,
    FullRed
}
