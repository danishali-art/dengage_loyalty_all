using System.Text.Json;
using FluentAssertions;
using dEngage.Loyalty.RuleEngine;
using dEngage.Loyalty.RuleEngine.Models;
using Xunit;

namespace dEngage.Loyalty.Engine.Tests;

// CR-05 (docs/scope-change-rules A5): the grouped AND/OR condition tree used by Rules/Card
// Buckets — see GroupedConditionEvaluator.cs.
public sealed class GroupedConditionEvaluatorTests
{
    private static EvaluationEvent Event(object data, DateTime? occurredAt = null) => new()
    {
        EventType = "card.transaction",
        ContactKey = "c1",
        OccurredAt = occurredAt ?? DateTime.UtcNow,
        Data = JsonSerializer.SerializeToElement(data)
    };

    private static ConditionLeaf Leaf(string field, string op, string type, object data) => new()
    {
        Field = field,
        Operator = op,
        Value = new ConditionValue { Type = type, Data = JsonSerializer.SerializeToElement(data) }
    };

    private static ConditionTree Tree(string rootOp, params ConditionGroup[] groups) =>
        new() { Op = rootOp, Groups = groups.ToList() };

    private static ConditionGroup Group(string op, params ConditionLeaf[] leaves) =>
        new() { Op = op, Conditions = leaves.ToList() };

    [Fact]
    public void Null_tree_matches_everything()
    {
        GroupedConditionEvaluator.Evaluate(null, Event(new { }), ConditionContext.Empty).Should().BeTrue();
    }

    [Fact]
    public void AND_root_requires_every_group_to_pass()
    {
        var tree = Tree(GroupedConditionOps.And,
            Group(GroupedConditionOps.And, Leaf("mcc", GroupedConditionOps.Eq, "string", "5411")),
            Group(GroupedConditionOps.And, Leaf("mcc", GroupedConditionOps.Eq, "string", "5541")));

        GroupedConditionEvaluator.Evaluate(tree, Event(new { mcc = "5411" }), ConditionContext.Empty).Should().BeFalse();
    }

    [Fact]
    public void OR_root_requires_only_one_group_to_pass()
    {
        var tree = Tree(GroupedConditionOps.Or,
            Group(GroupedConditionOps.And, Leaf("mcc", GroupedConditionOps.Eq, "string", "5411")),
            Group(GroupedConditionOps.And, Leaf("mcc", GroupedConditionOps.Eq, "string", "5541")));

        GroupedConditionEvaluator.Evaluate(tree, Event(new { mcc = "5541" }), ConditionContext.Empty).Should().BeTrue();
    }

    [Theory]
    [InlineData(GroupedConditionOps.NotIn, true)]
    [InlineData(GroupedConditionOps.Neq, true)]
    [InlineData(GroupedConditionOps.IsNull, true)]
    [InlineData(GroupedConditionOps.Eq, false)]
    [InlineData(GroupedConditionOps.Gte, false)]
    public void Missing_field_semantics_match_A5(string op, bool expected)
    {
        var leaf = op switch
        {
            GroupedConditionOps.NotIn => Leaf("missing", op, "string[]", new[] { "x" }),
            GroupedConditionOps.IsNull => Leaf("missing", op, "none", ""),
            _ => Leaf("missing", op, "string", "x")
        };
        var tree = Tree(GroupedConditionOps.And, Group(GroupedConditionOps.And, leaf));

        GroupedConditionEvaluator.Evaluate(tree, Event(new { }), ConditionContext.Empty).Should().Be(expected);
    }

    [Fact]
    public void Between_is_inclusive_both_ends()
    {
        var leaf = Leaf("amount", GroupedConditionOps.Between, "money", new decimal[] { 100, 500 });
        var tree = Tree(GroupedConditionOps.And, Group(GroupedConditionOps.And, leaf));

        GroupedConditionEvaluator.Evaluate(tree, Event(new { amount = 100m }), ConditionContext.Empty).Should().BeTrue();
        GroupedConditionEvaluator.Evaluate(tree, Event(new { amount = 500m }), ConditionContext.Empty).Should().BeTrue();
        GroupedConditionEvaluator.Evaluate(tree, Event(new { amount = 501m }), ConditionContext.Empty).Should().BeFalse();
    }

    [Fact]
    public void Agg_tier_resolves_from_context_not_payload()
    {
        var leaf = Leaf("agg.tier", GroupedConditionOps.Eq, "string", "gold");
        var tree = Tree(GroupedConditionOps.And, Group(GroupedConditionOps.And, leaf));
        var context = new ConditionContext { CustomerTierName = "gold" };

        // Payload carries a DIFFERENT tier — must not be read from profile.* data, only context.
        GroupedConditionEvaluator.Evaluate(tree, Event(new { profile = new { tier = "bronze" } }), context).Should().BeTrue();
    }

    [Fact]
    public void Agg_hoursSince_replaces_occurred_within()
    {
        var leaf = Leaf("agg.hoursSince.signup", GroupedConditionOps.Lte, "number", 1);
        var tree = Tree(GroupedConditionOps.And, Group(GroupedConditionOps.And, leaf));
        var now = DateTime.UtcNow;
        var context = new ConditionContext
        {
            LatestEventAt = new Dictionary<string, DateTime> { ["signup"] = now.AddMinutes(-30) }
        };

        GroupedConditionEvaluator.Evaluate(tree, Event(new { }, now), context).Should().BeTrue();

        var farContext = new ConditionContext
        {
            LatestEventAt = new Dictionary<string, DateTime> { ["signup"] = now.AddHours(-2) }
        };
        GroupedConditionEvaluator.Evaluate(tree, Event(new { }, now), farContext).Should().BeFalse();
    }
}

public sealed class GroupedConditionDslTests
{
    [Fact]
    public void Unsatisfiable_AND_bound_is_rejected()
    {
        var tree = new ConditionTree
        {
            Op = GroupedConditionOps.And,
            Groups = new List<ConditionGroup>
            {
                new()
                {
                    Op = GroupedConditionOps.And,
                    Conditions = new List<ConditionLeaf>
                    {
                        new() { Field = "amount", Operator = GroupedConditionOps.Gte, Value = new ConditionValue { Type = "money", Data = JsonSerializer.SerializeToElement(500) } },
                        new() { Field = "amount", Operator = GroupedConditionOps.Lte, Value = new ConditionValue { Type = "money", Data = JsonSerializer.SerializeToElement(100) } }
                    }
                }
            }
        };

        var act = () => GroupedConditionDsl.Validate(tree);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Same_bound_in_an_OR_group_is_allowed()
    {
        var tree = new ConditionTree
        {
            Op = GroupedConditionOps.And,
            Groups = new List<ConditionGroup>
            {
                new()
                {
                    Op = GroupedConditionOps.Or,
                    Conditions = new List<ConditionLeaf>
                    {
                        new() { Field = "amount", Operator = GroupedConditionOps.Gte, Value = new ConditionValue { Type = "money", Data = JsonSerializer.SerializeToElement(500) } },
                        new() { Field = "amount", Operator = GroupedConditionOps.Lte, Value = new ConditionValue { Type = "money", Data = JsonSerializer.SerializeToElement(100) } }
                    }
                }
            }
        };

        var act = () => GroupedConditionDsl.Validate(tree);
        act.Should().NotThrow();
    }
}

public sealed class FlatConditionsMigratorTests
{
    [Fact]
    public void Occurred_within_maps_to_agg_hoursSince()
    {
        var flat = new List<ConditionClause>
        {
            new()
            {
                Field = "event",
                Op = ConditionOps.OccurredWithin,
                Value = JsonSerializer.SerializeToElement(new { after_event = "signup", hours = 1.0 })
            }
        };

        var tree = FlatConditionsMigrator.ToGroupedTree(flat)!;
        var leaf = tree.Groups.Single().Conditions.Single();

        leaf.Field.Should().Be("agg.hoursSince.signup");
        leaf.Operator.Should().Be(GroupedConditionOps.Lte);
    }

    [Fact]
    public void Bare_tier_field_maps_to_agg_tier()
    {
        var flat = new List<ConditionClause>
        {
            new() { Field = "tier", Op = ConditionOps.Eq, Value = JsonSerializer.SerializeToElement("gold") }
        };

        var tree = FlatConditionsMigrator.ToGroupedTree(flat)!;
        tree.Groups.Single().Conditions.Single().Field.Should().Be("agg.tier");
    }

    [Theory]
    [InlineData(ConditionOps.Gt, GroupedConditionOps.Gte)]
    [InlineData(ConditionOps.Lt, GroupedConditionOps.Lte)]
    public void Strict_gt_lt_fold_into_gte_lte(string oldOp, string newOp)
    {
        var flat = new List<ConditionClause>
        {
            new() { Field = "amount", Op = oldOp, Value = JsonSerializer.SerializeToElement(100) }
        };

        var tree = FlatConditionsMigrator.ToGroupedTree(flat)!;
        tree.Groups.Single().Conditions.Single().Operator.Should().Be(newOp);
    }

    [Fact]
    public void Null_or_empty_input_yields_null_tree()
    {
        FlatConditionsMigrator.ToGroupedTree(null).Should().BeNull();
        FlatConditionsMigrator.ToGroupedTree(new List<ConditionClause>()).Should().BeNull();
    }

    [Fact]
    public void Everything_wraps_in_a_single_AND_group_under_an_AND_root()
    {
        var flat = new List<ConditionClause>
        {
            new() { Field = "mcc", Op = ConditionOps.In, Value = JsonSerializer.SerializeToElement(new[] { "5411" }) },
            new() { Field = "amount", Op = ConditionOps.Gte, Value = JsonSerializer.SerializeToElement(100) }
        };

        var tree = FlatConditionsMigrator.ToGroupedTree(flat)!;
        tree.Op.Should().Be(GroupedConditionOps.And);
        tree.Groups.Should().HaveCount(1);
        tree.Groups[0].Op.Should().Be(GroupedConditionOps.And);
        tree.Groups[0].Conditions.Should().HaveCount(2);
    }

    // Regression coverage for a real production incident: RuleCacheService.LoadFromDbAsync (and
    // the Rules/CardBuckets admin API) started deserializing `rules.conditions` straight into
    // the new grouped ConditionTree shape with no fallback — every tenant's pre-existing rule,
    // whose conditions column still held the old flat array above, threw and was silently
    // skipped by the matcher. ParseConditions is the shared fix these tests cover: detect which
    // shape a row is actually in and read it correctly either way.

    [Fact]
    public void ParseConditions_converts_old_flat_array_shape_and_reports_wasFlat()
    {
        const string flatJson = """[{"field":"tier","op":"in","value":["gold"]}]""";

        var tree = FlatConditionsMigrator.ParseConditions(flatJson, out var wasFlat);

        wasFlat.Should().BeTrue();
        var leaf = tree!.Groups.Single().Conditions.Single();
        leaf.Field.Should().Be("agg.tier");
        leaf.Operator.Should().Be(GroupedConditionOps.In);
    }

    [Fact]
    public void ParseConditions_reads_the_new_grouped_shape_unchanged_and_reports_not_wasFlat()
    {
        const string groupedJson = """
            {"op":"AND","groups":[{"op":"AND","conditions":[
                {"field":"amount","operator":"gte","value":{"type":"money","data":100}}
            ]}]}
            """;

        var tree = FlatConditionsMigrator.ParseConditions(groupedJson, out var wasFlat);

        wasFlat.Should().BeFalse();
        tree!.Groups[0].Conditions[0].Field.Should().Be("amount");
    }

    [Fact]
    public void ParseConditions_returns_null_for_null_input()
    {
        FlatConditionsMigrator.ParseConditions(null, out var wasFlat).Should().BeNull();
        wasFlat.Should().BeFalse();
    }

    [Fact]
    public void ParseConditions_single_arg_overload_matches_the_out_arg_overload()
    {
        const string flatJson = """[{"field":"channel","op":"eq","value":"mobile"}]""";

        var viaOutArg = FlatConditionsMigrator.ParseConditions(flatJson, out _);
        var viaPlain = FlatConditionsMigrator.ParseConditions(flatJson);

        JsonSerializer.Serialize(viaPlain).Should().Be(JsonSerializer.Serialize(viaOutArg));
    }

    [Fact]
    public void ParseConditions_empty_flat_array_yields_null_tree_but_still_reports_wasFlat()
    {
        var tree = FlatConditionsMigrator.ParseConditions("[]", out var wasFlat);

        wasFlat.Should().BeTrue();
        tree.Should().BeNull();
    }
}
