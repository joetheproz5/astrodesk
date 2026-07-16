using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional
#pragma warning disable CA1861 // EF-generated migration uses inline column arrays

namespace AstroDesk.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    ValueType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                    table.CheckConstraint("CK_AppSetting_UpdatedAt", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_AppSettings_Key_NotBlank", "length(trim(\"Key\")) > 0");
                    table.CheckConstraint("CK_AppSettings_ValueType_NotBlank", "length(trim(\"ValueType\")) > 0");
                });

            migrationBuilder.CreateTable(
                name: "EquipmentProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    Camera = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Lens = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Tripod = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Accessories = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentProfiles", x => x.Id);
                    table.CheckConstraint("CK_EquipmentProfile_UpdatedAt", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_EquipmentProfiles_Camera_NotBlank", "length(trim(\"Camera\")) > 0");
                    table.CheckConstraint("CK_EquipmentProfiles_Lens_NotBlank", "length(trim(\"Lens\")) > 0");
                    table.CheckConstraint("CK_EquipmentProfiles_Name_NotBlank", "length(trim(\"Name\")) > 0");
                });

            migrationBuilder.CreateTable(
                name: "OverlayPresets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    ShowRuleOfThirds = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowCenterCrosshair = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowDiagonalGuides = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowSafeArea = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowHorizon = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowCustomRectangle = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowCircle = table.Column<bool>(type: "INTEGER", nullable: false),
                    Opacity = table.Column<double>(type: "REAL", nullable: false),
                    LineThickness = table.Column<double>(type: "REAL", nullable: false),
                    ColorHex = table.Column<string>(type: "TEXT", maxLength: 9, nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OverlayPresets", x => x.Id);
                    table.CheckConstraint("CK_OverlayPreset_UpdatedAt", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_OverlayPresets_ColorHex", "(length(\"ColorHex\") = 7 AND \"ColorHex\" GLOB '#[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]') OR (length(\"ColorHex\") = 9 AND \"ColorHex\" GLOB '#[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]')");
                    table.CheckConstraint("CK_OverlayPresets_LineThickness", "\"LineThickness\" > 0.0 AND \"LineThickness\" <= 20.0");
                    table.CheckConstraint("CK_OverlayPresets_Name_NotBlank", "length(trim(\"Name\")) > 0");
                    table.CheckConstraint("CK_OverlayPresets_Opacity", "\"Opacity\" >= 0.0 AND \"Opacity\" <= 1.0");
                });

            migrationBuilder.CreateTable(
                name: "SavedLocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    Latitude = table.Column<double>(type: "REAL", nullable: false),
                    Longitude = table.Column<double>(type: "REAL", nullable: false),
                    ElevationMeters = table.Column<double>(type: "REAL", nullable: true),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedLocations", x => x.Id);
                    table.CheckConstraint("CK_SavedLocation_UpdatedAt", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_SavedLocations_Latitude", "\"Latitude\" >= -90.0 AND \"Latitude\" <= 90.0");
                    table.CheckConstraint("CK_SavedLocations_Longitude", "\"Longitude\" >= -180.0 AND \"Longitude\" <= 180.0");
                    table.CheckConstraint("CK_SavedLocations_Name_NotBlank", "length(trim(\"Name\")) > 0");
                });

            migrationBuilder.CreateTable(
                name: "ShootingSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    SessionType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    StartTime = table.Column<long>(type: "INTEGER", nullable: true),
                    EndTime = table.Column<long>(type: "INTEGER", nullable: true),
                    PausedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    TotalPausedDuration = table.Column<long>(type: "INTEGER", nullable: false),
                    LocationName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    Latitude = table.Column<double>(type: "REAL", nullable: false),
                    Longitude = table.Column<double>(type: "REAL", nullable: false),
                    Camera = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SelectedPhoneLens = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Iso = table.Column<int>(type: "INTEGER", nullable: true),
                    ExposureTime = table.Column<long>(type: "INTEGER", nullable: false),
                    DelayBetweenFrames = table.Column<long>(type: "INTEGER", nullable: false),
                    InitialDelay = table.Column<long>(type: "INTEGER", nullable: false),
                    WhiteBalance = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FocusSetting = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RawEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    FrameCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PlannedFrameCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Problems = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Rating = table.Column<int>(type: "INTEGER", nullable: true),
                    BatteryPercentageAtStart = table.Column<int>(type: "INTEGER", nullable: true),
                    BatteryPercentageAtEnd = table.Column<int>(type: "INTEGER", nullable: true),
                    StorageBytesAtStart = table.Column<long>(type: "INTEGER", nullable: true),
                    StorageBytesAtEnd = table.Column<long>(type: "INTEGER", nullable: true),
                    EquipmentProfileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CurrentSessionSlot = table.Column<int>(type: "INTEGER", nullable: true, computedColumnSql: "CASE WHEN \"Status\" IN ('Active','Paused') THEN 1 ELSE NULL END", stored: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShootingSessions", x => x.Id);
                    table.CheckConstraint("CK_ShootingSession_UpdatedAt", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_ShootingSessions_Battery", "(\"BatteryPercentageAtStart\" IS NULL OR (\"BatteryPercentageAtStart\" >= 0 AND \"BatteryPercentageAtStart\" <= 100)) AND (\"BatteryPercentageAtEnd\" IS NULL OR (\"BatteryPercentageAtEnd\" >= 0 AND \"BatteryPercentageAtEnd\" <= 100))");
                    table.CheckConstraint("CK_ShootingSessions_Delays", "\"DelayBetweenFrames\" >= 0 AND \"InitialDelay\" >= 0");
                    table.CheckConstraint("CK_ShootingSessions_ExposureTime", "\"ExposureTime\" > 0");
                    table.CheckConstraint("CK_ShootingSessions_FrameCounts", "\"FrameCount\" >= 0 AND \"PlannedFrameCount\" >= 0");
                    table.CheckConstraint("CK_ShootingSessions_Iso", "\"Iso\" IS NULL OR \"Iso\" > 0");
                    table.CheckConstraint("CK_ShootingSessions_Latitude", "\"Latitude\" >= -90.0 AND \"Latitude\" <= 90.0");
                    table.CheckConstraint("CK_ShootingSessions_LocationName_NotBlank", "length(trim(\"LocationName\")) > 0");
                    table.CheckConstraint("CK_ShootingSessions_Longitude", "\"Longitude\" >= -180.0 AND \"Longitude\" <= 180.0");
                    table.CheckConstraint("CK_ShootingSessions_Rating", "\"Rating\" IS NULL OR (\"Rating\" >= 1 AND \"Rating\" <= 5)");
                    table.CheckConstraint("CK_ShootingSessions_SessionType", "\"SessionType\" IN ('MilkyWay','Starscape','StarTrails','Moon','Planet','Constellation','DeepSky','Timelapse','TestSession','Other')");
                    table.CheckConstraint("CK_ShootingSessions_Status", "\"Status\" IN ('Planned','Active','Paused','Completed')");
                    table.CheckConstraint("CK_ShootingSessions_StatusTimeline", "(\"Status\" = 'Planned' AND \"StartTime\" IS NULL AND \"EndTime\" IS NULL AND \"PausedAt\" IS NULL) OR (\"Status\" = 'Active' AND \"StartTime\" IS NOT NULL AND \"EndTime\" IS NULL AND \"PausedAt\" IS NULL) OR (\"Status\" = 'Paused' AND \"StartTime\" IS NOT NULL AND \"EndTime\" IS NULL AND \"PausedAt\" IS NOT NULL) OR (\"Status\" = 'Completed' AND \"StartTime\" IS NOT NULL AND \"EndTime\" IS NOT NULL AND \"PausedAt\" IS NULL)");
                    table.CheckConstraint("CK_ShootingSessions_Storage", "(\"StorageBytesAtStart\" IS NULL OR \"StorageBytesAtStart\" >= 0) AND (\"StorageBytesAtEnd\" IS NULL OR \"StorageBytesAtEnd\" >= 0)");
                    table.CheckConstraint("CK_ShootingSessions_TargetName_NotBlank", "length(trim(\"TargetName\")) > 0");
                    table.CheckConstraint("CK_ShootingSessions_Timeline", "(\"StartTime\" IS NULL OR \"StartTime\" >= \"CreatedAt\") AND (\"EndTime\" IS NULL OR (\"StartTime\" IS NOT NULL AND \"EndTime\" >= \"StartTime\")) AND (\"PausedAt\" IS NULL OR (\"StartTime\" IS NOT NULL AND \"PausedAt\" >= \"StartTime\"))");
                    table.CheckConstraint("CK_ShootingSessions_TotalPausedDuration", "\"TotalPausedDuration\" >= 0");
                    table.ForeignKey(
                        name: "FK_ShootingSessions_EquipmentProfiles_EquipmentProfileId",
                        column: x => x.EquipmentProfileId,
                        principalTable: "EquipmentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SessionAstronomySnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShootingSessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CapturedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Sunset = table.Column<long>(type: "INTEGER", nullable: true),
                    EndOfAstronomicalTwilight = table.Column<long>(type: "INTEGER", nullable: true),
                    Sunrise = table.Column<long>(type: "INTEGER", nullable: true),
                    StartOfAstronomicalTwilight = table.Column<long>(type: "INTEGER", nullable: true),
                    MoonPhase = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    MoonIlluminationPercent = table.Column<double>(type: "REAL", nullable: true),
                    Moonrise = table.Column<long>(type: "INTEGER", nullable: true),
                    Moonset = table.Column<long>(type: "INTEGER", nullable: true),
                    MoonAltitudeDegrees = table.Column<double>(type: "REAL", nullable: true),
                    DarkSkyWindowStart = table.Column<long>(type: "INTEGER", nullable: true),
                    DarkSkyWindowEnd = table.Column<long>(type: "INTEGER", nullable: true),
                    ProviderName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionAstronomySnapshots", x => x.Id);
                    table.CheckConstraint("CK_SessionAstronomySnapshot_UpdatedAt", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_SessionAstronomySnapshots_DarkSkyWindow", "\"DarkSkyWindowStart\" IS NULL OR \"DarkSkyWindowEnd\" IS NULL OR \"DarkSkyWindowEnd\" >= \"DarkSkyWindowStart\"");
                    table.CheckConstraint("CK_SessionAstronomySnapshots_MoonAltitude", "\"MoonAltitudeDegrees\" IS NULL OR (\"MoonAltitudeDegrees\" >= -90.0 AND \"MoonAltitudeDegrees\" <= 90.0)");
                    table.CheckConstraint("CK_SessionAstronomySnapshots_MoonIllumination", "\"MoonIlluminationPercent\" IS NULL OR (\"MoonIlluminationPercent\" >= 0.0 AND \"MoonIlluminationPercent\" <= 100.0)");
                    table.ForeignKey(
                        name: "FK_SessionAstronomySnapshots_ShootingSessions_ShootingSessionId",
                        column: x => x.ShootingSessionId,
                        principalTable: "ShootingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SessionNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShootingSessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    NotedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionNotes", x => x.Id);
                    table.CheckConstraint("CK_SessionNote_UpdatedAt", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_SessionNotes_Content_NotBlank", "length(trim(\"Content\")) > 0");
                    table.CheckConstraint("CK_SessionNotes_Kind", "\"Kind\" IN ('General','Observation','Problem')");
                    table.ForeignKey(
                        name: "FK_SessionNotes_ShootingSessions_ShootingSessionId",
                        column: x => x.ShootingSessionId,
                        principalTable: "ShootingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SessionScreenshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShootingSessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    IncludesOverlays = table.Column<bool>(type: "INTEGER", nullable: false),
                    CapturedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ImageFormat = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionScreenshots", x => x.Id);
                    table.CheckConstraint("CK_SessionScreenshot_UpdatedAt", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_SessionScreenshots_FilePath_NotBlank", "length(trim(\"FilePath\")) > 0");
                    table.CheckConstraint("CK_SessionScreenshots_ImageFormat_NotBlank", "length(trim(\"ImageFormat\")) > 0");
                    table.ForeignKey(
                        name: "FK_SessionScreenshots_ShootingSessions_ShootingSessionId",
                        column: x => x.ShootingSessionId,
                        principalTable: "ShootingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SessionWeatherSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShootingSessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CapturedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    TemperatureCelsius = table.Column<double>(type: "REAL", nullable: true),
                    HumidityPercent = table.Column<double>(type: "REAL", nullable: true),
                    WindSpeedKilometersPerHour = table.Column<double>(type: "REAL", nullable: true),
                    CloudCoverPercent = table.Column<double>(type: "REAL", nullable: true),
                    VisibilityKilometers = table.Column<double>(type: "REAL", nullable: true),
                    DewPointCelsius = table.Column<double>(type: "REAL", nullable: true),
                    DewRisk = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ProviderName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionWeatherSnapshots", x => x.Id);
                    table.CheckConstraint("CK_SessionWeatherSnapshot_UpdatedAt", "\"UpdatedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_SessionWeatherSnapshots_CloudCover", "\"CloudCoverPercent\" IS NULL OR (\"CloudCoverPercent\" >= 0.0 AND \"CloudCoverPercent\" <= 100.0)");
                    table.CheckConstraint("CK_SessionWeatherSnapshots_DewRisk", "\"DewRisk\" IN ('Unavailable','Low','Moderate','High')");
                    table.CheckConstraint("CK_SessionWeatherSnapshots_Humidity", "\"HumidityPercent\" IS NULL OR (\"HumidityPercent\" >= 0.0 AND \"HumidityPercent\" <= 100.0)");
                    table.CheckConstraint("CK_SessionWeatherSnapshots_Visibility", "\"VisibilityKilometers\" IS NULL OR \"VisibilityKilometers\" >= 0.0");
                    table.CheckConstraint("CK_SessionWeatherSnapshots_WindSpeed", "\"WindSpeedKilometersPerHour\" IS NULL OR \"WindSpeedKilometersPerHour\" >= 0.0");
                    table.ForeignKey(
                        name: "FK_SessionWeatherSnapshots_ShootingSessions_ShootingSessionId",
                        column: x => x.ShootingSessionId,
                        principalTable: "ShootingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "SavedLocations",
                columns: new[] { "Id", "CreatedAt", "ElevationMeters", "IsDefault", "Latitude", "Longitude", "Name", "TimeZoneId", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("177ce03d-d347-4471-aa63-ad942906396d"), 639197568000000000L, 1450.0, false, 34.209000000000003, 35.920000000000002, "Tannourine", "Asia/Beirut", 639197568000000000L },
                    { new Guid("1eb6fb77-e935-4f2f-b635-c18474befb25"), 639197568000000000L, 0.0, false, 33.893799999999999, 35.501800000000003, "Beirut", "Asia/Beirut", 639197568000000000L },
                    { new Guid("3685a745-3e51-4824-bf11-ed51e6581b84"), 639197568000000000L, 1850.0, true, 34.011099999999999, 35.828499999999998, "Faraya", "Asia/Beirut", 639197568000000000L },
                    { new Guid("499b960b-71bb-4393-82bb-d71e68958a22"), 639197568000000000L, 950.0, false, 33.541699999999999, 35.584400000000002, "Jezzine", "Asia/Beirut", 639197568000000000L },
                    { new Guid("ad8f2bee-f3fd-4f2c-ab40-416ff6df6738"), 639197568000000000L, 900.0, false, 33.846299999999999, 35.902000000000001, "Zahle", "Asia/Beirut", 639197568000000000L },
                    { new Guid("da59de93-b9da-451a-a784-0508817005fc"), 639197568000000000L, 900.0, false, 33.850000000000001, 35.899999999999999, "Bekaa Valley", "Asia/Beirut", 639197568000000000L }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_CreatedAt",
                table: "AppSettings",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_Key",
                table: "AppSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_UpdatedAt",
                table: "AppSettings",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentProfiles_CreatedAt",
                table: "EquipmentProfiles",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentProfiles_IsDefault",
                table: "EquipmentProfiles",
                column: "IsDefault",
                unique: true,
                filter: "\"IsDefault\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentProfiles_Name",
                table: "EquipmentProfiles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentProfiles_UpdatedAt",
                table: "EquipmentProfiles",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OverlayPresets_CreatedAt",
                table: "OverlayPresets",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OverlayPresets_IsDefault",
                table: "OverlayPresets",
                column: "IsDefault",
                unique: true,
                filter: "\"IsDefault\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_OverlayPresets_Name",
                table: "OverlayPresets",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OverlayPresets_UpdatedAt",
                table: "OverlayPresets",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SavedLocations_CreatedAt",
                table: "SavedLocations",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SavedLocations_IsDefault",
                table: "SavedLocations",
                column: "IsDefault",
                unique: true,
                filter: "\"IsDefault\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_SavedLocations_Name",
                table: "SavedLocations",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavedLocations_UpdatedAt",
                table: "SavedLocations",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SessionAstronomySnapshots_CapturedAt",
                table: "SessionAstronomySnapshots",
                column: "CapturedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SessionAstronomySnapshots_CreatedAt",
                table: "SessionAstronomySnapshots",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SessionAstronomySnapshots_ShootingSessionId",
                table: "SessionAstronomySnapshots",
                column: "ShootingSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionAstronomySnapshots_UpdatedAt",
                table: "SessionAstronomySnapshots",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SessionNotes_CreatedAt",
                table: "SessionNotes",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SessionNotes_NotedAt",
                table: "SessionNotes",
                column: "NotedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SessionNotes_ShootingSessionId",
                table: "SessionNotes",
                column: "ShootingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionNotes_ShootingSessionId_NotedAt",
                table: "SessionNotes",
                columns: new[] { "ShootingSessionId", "NotedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SessionNotes_UpdatedAt",
                table: "SessionNotes",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SessionScreenshots_CapturedAt",
                table: "SessionScreenshots",
                column: "CapturedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SessionScreenshots_CreatedAt",
                table: "SessionScreenshots",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SessionScreenshots_ShootingSessionId",
                table: "SessionScreenshots",
                column: "ShootingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionScreenshots_ShootingSessionId_FilePath",
                table: "SessionScreenshots",
                columns: new[] { "ShootingSessionId", "FilePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionScreenshots_UpdatedAt",
                table: "SessionScreenshots",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SessionWeatherSnapshots_CapturedAt",
                table: "SessionWeatherSnapshots",
                column: "CapturedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SessionWeatherSnapshots_CreatedAt",
                table: "SessionWeatherSnapshots",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SessionWeatherSnapshots_ShootingSessionId",
                table: "SessionWeatherSnapshots",
                column: "ShootingSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionWeatherSnapshots_UpdatedAt",
                table: "SessionWeatherSnapshots",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ShootingSessions_CreatedAt",
                table: "ShootingSessions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ShootingSessions_CurrentSessionSlot",
                table: "ShootingSessions",
                column: "CurrentSessionSlot",
                unique: true,
                filter: "\"CurrentSessionSlot\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ShootingSessions_EndTime",
                table: "ShootingSessions",
                column: "EndTime");

            migrationBuilder.CreateIndex(
                name: "IX_ShootingSessions_EquipmentProfileId",
                table: "ShootingSessions",
                column: "EquipmentProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ShootingSessions_LocationName",
                table: "ShootingSessions",
                column: "LocationName");

            migrationBuilder.CreateIndex(
                name: "IX_ShootingSessions_Rating",
                table: "ShootingSessions",
                column: "Rating");

            migrationBuilder.CreateIndex(
                name: "IX_ShootingSessions_SessionType",
                table: "ShootingSessions",
                column: "SessionType");

            migrationBuilder.CreateIndex(
                name: "IX_ShootingSessions_StartTime",
                table: "ShootingSessions",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_ShootingSessions_Status_StartTime",
                table: "ShootingSessions",
                columns: new[] { "Status", "StartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_ShootingSessions_TargetName",
                table: "ShootingSessions",
                column: "TargetName");

            migrationBuilder.CreateIndex(
                name: "IX_ShootingSessions_UpdatedAt",
                table: "ShootingSessions",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "OverlayPresets");

            migrationBuilder.DropTable(
                name: "SavedLocations");

            migrationBuilder.DropTable(
                name: "SessionAstronomySnapshots");

            migrationBuilder.DropTable(
                name: "SessionNotes");

            migrationBuilder.DropTable(
                name: "SessionScreenshots");

            migrationBuilder.DropTable(
                name: "SessionWeatherSnapshots");

            migrationBuilder.DropTable(
                name: "ShootingSessions");

            migrationBuilder.DropTable(
                name: "EquipmentProfiles");
        }
    }
}
