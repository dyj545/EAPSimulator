using EAPSimulator.Core.Protocols.Bridge;
using Xunit;

namespace EAPSimulator.Core.Tests;

/// <summary>
/// Tests the bridge mapping config: JSON round-trip, ApplyTo merging into the live mapper,
/// and the enable/disable filter. UI tests stay manual — these cover the data side so editing
/// the file by hand or via the GUI both produce the same in-memory state.
/// </summary>
public class MappingConfigTests
{
    [Fact]
    public void Save_Load_RoundTripsAllFields()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var cfg = new MappingConfig
            {
                Groups =
                {
                    new MappingGroup
                    {
                        Name = "EventReport",
                        SecsTemplate = "S6F11",
                        HostTemplate = "EventReport",
                        Description = "wafer move",
                        Enabled = true,
                        Mappings =
                        {
                            new FieldMapping
                            {
                                Source = FieldMappingSource.Secs,
                                Target = FieldMappingTarget.Host,
                                SecsPath = "1/0",
                                HostFieldName = "lotId",
                                Conversion = DataConversion.Trim,
                                Description = "lot id",
                            },
                        },
                    },
                },
            };
            cfg.Save(tmp);

            var loaded = MappingConfig.Load(tmp);
            var g = Assert.Single(loaded.Groups);
            Assert.Equal("EventReport", g.Name);
            Assert.Equal("S6F11", g.SecsTemplate);
            Assert.Equal("EventReport", g.HostTemplate);
            Assert.True(g.Enabled);
            var m = Assert.Single(g.Mappings);
            Assert.Equal("1/0", m.SecsPath);
            Assert.Equal("lotId", m.HostFieldName);
            Assert.Equal(DataConversion.Trim, m.Conversion);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyConfig()
    {
        // Picking a path that doesn't exist must not throw — first-run users have no file yet.
        var cfg = MappingConfig.Load(Path.Combine(Path.GetTempPath(), "definitely-not-real.json"));
        Assert.NotNull(cfg);
        Assert.Empty(cfg.Groups);
    }

    [Fact]
    public void ApplyTo_FlattensAllEnabledGroupsAndIgnoresDisabled()
    {
        var cfg = new MappingConfig
        {
            Groups =
            {
                new MappingGroup
                {
                    Name = "g1", Enabled = true,
                    Mappings =
                    {
                        new FieldMapping { SecsPath = "0", HostFieldName = "a" },
                        new FieldMapping { SecsPath = "1", HostFieldName = "b" },
                    },
                },
                new MappingGroup
                {
                    Name = "g2", Enabled = false,
                    Mappings = { new FieldMapping { SecsPath = "X", HostFieldName = "X" } },
                },
            },
        };
        var mapper = new DataMapper();
        // Pre-existing mapping should be cleared on apply.
        mapper.AddMapping(new FieldMapping { SecsPath = "old", HostFieldName = "old" });
        cfg.ApplyTo(mapper);
        Assert.Equal(2, mapper.Mappings.Count);
        Assert.DoesNotContain(mapper.Mappings, m => m.HostFieldName == "old");
        Assert.DoesNotContain(mapper.Mappings, m => m.HostFieldName == "X");
    }
}
