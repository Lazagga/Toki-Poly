using System;
using System.IO;
using System.Linq;
using GaeBullBing.Editor;
using NUnit.Framework;

namespace GaeBullBing.Tests.EditMode
{
    public sealed class RegionJsonParserTests
    {
        private const string Metadata = "{\"region_definitions\":[{\"id\":\"REGION_01\",\"name\":\"숲\",\"order\":1},{\"id\":\"REGION_02\",\"name\":\"설원\",\"order\":2}]}";

        [Test]
        public void ReadsRegionGimmicksAndUsesRegionMetadata()
        {
            var regions = RegionJsonImporter.Parse("{\"region_gimmicks\":[{\"region_id\":\"REGION_02\",\"name\":\"빙판 지형\",\"gimmick_id\":\"GIMMICK_ICE_SHEET\",\"status\":\"Confirmed\",\"type\":\"periodic_random_tile_status\",\"description\":\"빙판 생성\",\"parameters\":{\"applied_effect_id\":\"tile_freeze\",\"scope\":\"per_line\",\"tiles_per_turn\":3}}]}", Metadata);
            Assert.That(regions.Length, Is.EqualTo(1));
            var region = regions[0];
            Assert.That(region.Id, Is.EqualTo("REGION_02"));
            Assert.That(region.DisplayName, Is.EqualTo("설원"));
            Assert.That(region.Order, Is.EqualTo(2));
            Assert.That(region.GimmickId, Is.EqualTo("GIMMICK_ICE_SHEET"));
            Assert.That(region.Status, Is.EqualTo("Confirmed"));
            Assert.That(region.Type, Is.EqualTo("periodic_random_tile_status"));
            Assert.That(region.Description, Is.EqualTo("빙판 생성"));
            Assert.That(region.AppliedEffectId, Is.EqualTo("tile_freeze"));
            Assert.That(region.Scope, Is.EqualTo("per_line"));
            Assert.That(region.TilesPerTurn, Is.EqualTo(3));
            Assert.That(region.TargetTileIndex, Is.EqualTo(-1));
        }

        [Test]
        public void RegionWithoutGimmickOrParametersHasEmptyEffects()
        {
            var region = RegionJsonImporter.Parse("{\"region_gimmicks\":[{\"region_id\":\"REGION_01\",\"gimmick_id\":null,\"status\":\"None\"}]}", Metadata)[0];
            Assert.That(region.DisplayName, Is.EqualTo("숲"));
            Assert.That(region.GimmickId, Is.Empty);
            Assert.That(region.AppliedEffectId, Is.Empty);
            Assert.That(region.TargetTileIndex, Is.EqualTo(-1));
        }

        [Test]
        public void OverrideParametersAreCopiedWithoutChangingValues()
        {
            var region = RegionJsonImporter.Parse("{\"region_gimmicks\":[{\"region_id\":\"REGION_03\",\"parameters\":{\"target_tile_index\":18,\"override_effect_id\":\"oasis_extra_upgrade\",\"override_effect_value\":2}}]}", Metadata)[0];
            Assert.That(region.TargetTileIndex, Is.EqualTo(18));
            Assert.That(region.OverrideEffectId, Is.EqualTo("oasis_extra_upgrade"));
            Assert.That(region.OverrideEffectValue, Is.EqualTo(2));
        }

        [Test]
        public void OvergrowthNestedRestrictionsAndRewardAreParsed()
        {
            var region = RegionJsonImporter.Parse("{\"region_gimmicks\":[{\"region_id\":\"REGION_04\",\"parameters\":{\"scope\":\"board\",\"tiles_per_turn\":8,\"duration_turns\":1,\"restriction\":{\"disallow_build\":true,\"disallow_upgrade\":true},\"trigger_condition\":\"stop_exactly_on_tile\",\"reward_effect_id\":\"extra_turn\",\"reward_effect_value\":2}}]}", Metadata)[0];
            Assert.That(region.DurationTurns, Is.EqualTo(1));
            Assert.That(region.DisallowBuild && region.DisallowUpgrade, Is.True);
            Assert.That(region.TriggerCondition, Is.EqualTo("stop_exactly_on_tile"));
            Assert.That(region.RewardEffectId, Is.EqualTo("extra_turn"));
            Assert.That(region.RewardEffectValue, Is.EqualTo(2));
        }

        [Test]
        public void OasisTargetArrayAndRewardCountAreParsed()
        {
            var region = RegionJsonImporter.Parse("{\"region_gimmicks\":[{\"region_id\":\"REGION_03\",\"parameters\":{\"target_tile_indices\":[0,18],\"override_effect_id\":\"oasis_extra_upgrade\",\"override_effect_value\":2}}]}", Metadata)[0];
            Assert.That(region.TargetTileIndices, Is.EqualTo(new[] { 0, 18 }));
            Assert.That(region.OverrideEffectValue, Is.EqualTo(2));
        }

        [TestCase("{}")]
        [TestCase("{\"regions\":[{\"region_id\":\"REGION_01\"}]}")]
        [TestCase("{\"region_gimmicks\":[]}")]
        public void MissingOrEmptyCurrentArrayReportsActualJsonKey(string json)
        {
            var exception = Assert.Throws<InvalidOperationException>(() => RegionJsonImporter.Parse(json, Metadata));
            Assert.That(exception.Message, Does.Contain("region_gimmicks"));
        }

        [Test]
        public void MissingRegionIdIsRejected()
        {
            var exception = Assert.Throws<InvalidOperationException>(() => RegionJsonImporter.Parse("{\"region_gimmicks\":[{}]}", Metadata));
            Assert.That(exception.Message, Does.Contain("region_gimmicks[0]"));
        }

        [Test]
        public void DuplicateRegionIdIsRejected()
        {
            var exception = Assert.Throws<InvalidOperationException>(() => RegionJsonImporter.Parse("{\"region_gimmicks\":[{\"region_id\":\"REGION_01\"},{\"region_id\":\"REGION_01\"}]}", Metadata));
            Assert.That(exception.Message, Does.Contain("중복"));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("{invalid json")]
        public void InvalidJsonIsRejectedWithRegionFileContext(string json)
        {
            var exception = Assert.Throws<InvalidOperationException>(() => RegionJsonImporter.Parse(json, Metadata));
            Assert.That(exception.Message, Does.Contain("Region.json"));
        }

        [Test]
        public void CurrentJsonImportsEveryConfiguredRegionAndLatestParameters()
        {
            var regionJson = File.ReadAllText(RegionJsonImporter.JsonPath);
            var monsterJson = File.ReadAllText("Assets/GaeBullBing/Data/Json/Monster.json");
            var expected = UnityEngine.JsonUtility.FromJson<ExpectedRoot>(regionJson).region_gimmicks;
            var regions = RegionJsonImporter.Parse(regionJson, monsterJson);
            Assert.That(regions.Length, Is.EqualTo(expected.Length));
            foreach (var item in expected)
            {
                var region = regions.Single(value => value.Id == item.region_id);
                Assert.That(region.GimmickId, Is.EqualTo(item.gimmick_id ?? string.Empty));
                Assert.That(region.Description, Is.EqualTo(item.description ?? string.Empty));
                Assert.That(region.TilesPerTurn, Is.EqualTo(item.parameters?.tiles_per_turn ?? 0));
                Assert.That(region.Scope, Is.EqualTo(item.parameters?.scope ?? string.Empty));
            }
        }

        [Serializable] private sealed class ExpectedRoot { public ExpectedRegion[] region_gimmicks = Array.Empty<ExpectedRegion>(); }
        [Serializable] private sealed class ExpectedRegion
        {
            public string region_id = string.Empty, gimmick_id = string.Empty, description = string.Empty;
            public ExpectedParameters parameters = null;
        }
        [Serializable] private sealed class ExpectedParameters { public int tiles_per_turn = 0; public string scope = string.Empty; }
    }
}
