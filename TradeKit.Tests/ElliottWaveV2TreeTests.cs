using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.ElliottWave;

namespace TradeKit.Tests
{
    /// <summary>
    /// Step 2 of EW_MARKUP_v2.md §19 — the variant-tree node lifecycle: death cascade
    /// (§6.3), sibling independence (§6.3) and up-propagating backtracking (§6.5).
    /// </summary>
    [TestFixture]
    public class ElliottWaveV2TreeTests
    {
        private static TreeNode Node(
            ElliottModelType model = ElliottModelType.IMPULSE,
            string? pos = null,
            ChildRelation relation = ChildRelation.SEQUENCE) =>
            new(model, pos!, 0, relation);

        [Test]
        public void NewNode_IsOpenAndAlive()
        {
            TreeNode n = Node();
            Assert.That(n.Status, Is.EqualTo(NodeStatus.OPEN));
            Assert.That(n.IsAlive, Is.True);
            Assert.That(n.DeathReason, Is.EqualTo(DeathReason.NONE));
        }

        [Test]
        public void Kill_SetsStatusAndReason()
        {
            TreeNode n = Node();
            n.Kill(DeathReason.PRICE_BREACH);

            Assert.That(n.Status, Is.EqualTo(NodeStatus.DEAD));
            Assert.That(n.IsAlive, Is.False);
            Assert.That(n.DeathReason, Is.EqualTo(DeathReason.PRICE_BREACH));
        }

        [Test]
        public void Kill_CascadesDownToAllDescendants()
        {
            TreeNode root = Node(relation: ChildRelation.SEQUENCE);
            TreeNode child = root.AddChild(Node(pos: "1"));
            TreeNode grandChild = child.AddChild(Node(pos: "1"));

            root.Kill(DeathReason.PRICE_BREACH);

            Assert.That(root.DeathReason, Is.EqualTo(DeathReason.PRICE_BREACH));
            Assert.That(child.Status, Is.EqualTo(NodeStatus.DEAD));
            Assert.That(child.DeathReason, Is.EqualTo(DeathReason.PARENT_DEAD));
            Assert.That(grandChild.Status, Is.EqualTo(NodeStatus.DEAD));
            Assert.That(grandChild.DeathReason, Is.EqualTo(DeathReason.PARENT_DEAD));
        }

        [Test]
        public void Kill_DoesNotAffectSiblings_UnderAlternatives()
        {
            // §6.3: "4 = SIMPLE_IMPULSE" dies, but its brother "4 = TRIANGLE" stays alive.
            TreeNode decision = Node(relation: ChildRelation.ALTERNATIVES);
            TreeNode simple = decision.AddChild(Node(ElliottModelType.SIMPLE_IMPULSE, "4"));
            TreeNode triangle = decision.AddChild(Node(ElliottModelType.TRIANGLE_CONTRACTING, "4"));

            simple.Kill(DeathReason.PRICE_BREACH);

            Assert.That(simple.Status, Is.EqualTo(NodeStatus.DEAD));
            Assert.That(triangle.IsAlive, Is.True);
            Assert.That(decision.IsAlive, Is.True, "Parent must stay alive while an alternative remains.");
            Assert.That(decision.AliveChildrenCount, Is.EqualTo(1));
        }

        [Test]
        public void Alternatives_LastChildDeath_KillsParentWithNoValidSubmodel()
        {
            TreeNode decision = Node(relation: ChildRelation.ALTERNATIVES);
            TreeNode a = decision.AddChild(Node(ElliottModelType.SIMPLE_IMPULSE, "4"));
            TreeNode b = decision.AddChild(Node(ElliottModelType.TRIANGLE_CONTRACTING, "4"));

            a.Kill(DeathReason.PRICE_BREACH);
            Assert.That(decision.IsAlive, Is.True);

            b.Kill(DeathReason.PRICE_BREACH);

            Assert.That(decision.Status, Is.EqualTo(NodeStatus.DEAD));
            Assert.That(decision.DeathReason, Is.EqualTo(DeathReason.NO_VALID_SUBMODEL));
            Assert.That(decision.AliveChildrenCount, Is.EqualTo(0));
        }

        [Test]
        public void Sequence_ChildDeath_InvalidatesParentImmediately()
        {
            TreeNode impulse = Node(relation: ChildRelation.SEQUENCE);
            TreeNode w1 = impulse.AddChild(Node(pos: "1"));
            TreeNode w2 = impulse.AddChild(Node(pos: "2"));

            w2.Kill(DeathReason.PRICE_BREACH);

            Assert.That(impulse.Status, Is.EqualTo(NodeStatus.DEAD));
            Assert.That(impulse.DeathReason, Is.EqualTo(DeathReason.CHILD_INVALID));
            // The cascade also takes the surviving sibling w1 down.
            Assert.That(w1.Status, Is.EqualTo(NodeStatus.DEAD));
            Assert.That(w1.DeathReason, Is.EqualTo(DeathReason.PARENT_DEAD));
        }

        [Test]
        public void Backtracking_PropagatesUp_UntilLevelWithAliveVariant()
        {
            // root(ALT) -> { branchA(SEQ -> leafA), branchB(SEQ -> leafB) }
            // Killing leafA kills branchA but root survives via branchB.
            TreeNode root = Node(relation: ChildRelation.ALTERNATIVES);
            TreeNode branchA = root.AddChild(Node(relation: ChildRelation.SEQUENCE));
            TreeNode leafA = branchA.AddChild(Node(pos: "1"));
            TreeNode branchB = root.AddChild(Node(relation: ChildRelation.SEQUENCE));
            TreeNode leafB = branchB.AddChild(Node(pos: "1"));

            leafA.Kill(DeathReason.TIME_WINDOW);

            Assert.That(branchA.Status, Is.EqualTo(NodeStatus.DEAD));
            Assert.That(branchA.DeathReason, Is.EqualTo(DeathReason.CHILD_INVALID));
            Assert.That(root.IsAlive, Is.True, "root must survive through branchB.");
            Assert.That(branchB.IsAlive, Is.True);

            // Killing the second branch's leaf must now propagate to the root.
            leafB.Kill(DeathReason.TIME_WINDOW);

            Assert.That(branchB.Status, Is.EqualTo(NodeStatus.DEAD));
            Assert.That(root.Status, Is.EqualTo(NodeStatus.DEAD));
            Assert.That(root.DeathReason, Is.EqualTo(DeathReason.NO_VALID_SUBMODEL));
        }

        [Test]
        public void MarkComplete_OnDeadNode_Throws()
        {
            TreeNode n = Node();
            n.Kill(DeathReason.PRICE_BREACH);
            Assert.Throws<InvalidOperationException>(() => n.MarkComplete());
            Assert.Throws<InvalidOperationException>(() => n.MarkProjected());
        }

        [Test]
        public void AddChild_TwiceToDifferentParents_Throws()
        {
            TreeNode child = Node(pos: "1");
            Node().AddChild(child);
            Assert.Throws<InvalidOperationException>(() => Node().AddChild(child));
        }

        [Test]
        public void StartModels_AreTheImpulsiveRoots()
        {
            CollectionAssert.AreEquivalent(
                new[]
                {
                    ElliottModelType.IMPULSE,
                    ElliottModelType.DIAGONAL_CONTRACTING_INITIAL,
                    ElliottModelType.ZIGZAG,
                    ElliottModelType.DOUBLE_ZIGZAG
                },
                ElliottWaveExactMarkupV2.StartModels);
        }
    }
}
