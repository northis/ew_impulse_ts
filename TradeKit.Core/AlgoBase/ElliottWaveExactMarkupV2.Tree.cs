using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;

namespace TradeKit.Core.AlgoBase
{
    /// <summary>
    /// Lifecycle state of a <see cref="TreeNode"/> (see EW_MARKUP_v2.md §6.2).
    /// </summary>
    public enum NodeStatus
    {
        /// <summary>The node is still growing — waves are being attached.</summary>
        OPEN,

        /// <summary>All expected waves were gathered and are valid.</summary>
        COMPLETE,

        /// <summary>The node violated a hard rule and (with all descendants) is dead.</summary>
        DEAD,

        /// <summary>
        /// The node is incomplete but its range reached the current bar, so the missing
        /// tail is projected (see §13).
        /// </summary>
        PROJECTED
    }

    /// <summary>
    /// Reason a <see cref="TreeNode"/> died (see EW_MARKUP_v2.md §6.4).
    /// </summary>
    public enum DeathReason
    {
        /// <summary>The node is alive.</summary>
        NONE,

        /// <summary>Price pierced a forbidden level for the model/position (§7).</summary>
        PRICE_BREACH,

        /// <summary>A wave ended outside the allowed time window (§8).</summary>
        TIME_WINDOW,

        /// <summary>A junior wave is longer in time than its senior counterpart (§9).</summary>
        DURATION_ORDER,

        /// <summary>A <c>SIMPLE_IMPULSE</c> has an intra-segment overshoot (breaks I2, §10).</summary>
        SIMPLE_OVERSHOOT,

        /// <summary>An extension cancellation level/time triggered (§11).</summary>
        EXTENSION_CANCELLED,

        /// <summary>No valid sub-model remained for a mandatory position (§6.5).</summary>
        NO_VALID_SUBMODEL,

        /// <summary>A Fibonacci ratio fell outside the hard-allowed range (§16).</summary>
        RATIO_OUT_OF_RANGE,

        /// <summary>A parent died, so this descendant was killed in the cascade (§6.3).</summary>
        PARENT_DEAD,

        /// <summary>A mandatory sub-wave (Sequence child) died, invalidating this node.</summary>
        CHILD_INVALID
    }

    /// <summary>
    /// How a node relates to its children (drives the death/backtracking logic, §6.3/§6.5).
    /// </summary>
    public enum ChildRelation
    {
        /// <summary>
        /// Children are mutually-exclusive hypotheses (OR). The node stays alive while at
        /// least one child is alive and dies with <see cref="DeathReason.NO_VALID_SUBMODEL"/>
        /// when the last one dies.
        /// </summary>
        ALTERNATIVES,

        /// <summary>
        /// Children are mandatory sub-waves (AND). The death of any child invalidates the
        /// node, which dies with <see cref="DeathReason.CHILD_INVALID"/>.
        /// </summary>
        SEQUENCE
    }

    /// <summary>
    /// A node of the v2 markup variant tree — the hypothesis "segment range
    /// <c>[RangeStartSegment..RangeEndSegment]</c> is model <see cref="Model"/> in wave
    /// position <see cref="WavePos"/>" (see EW_MARKUP_v2.md §6).
    /// <para>
    /// This type implements <b>step 2 of §19</b>: the node structure, lifecycle, the
    /// down-cascade of death (§6.3) and the up-propagating backtracking (§6.5). The
    /// concrete hard rules that decide <i>when</i> a node dies are added in later steps.
    /// </para>
    /// </summary>
    public sealed class TreeNode
    {
        private static int _sNextId;

        private readonly List<TreeNode> m_Children = new();
        private int m_AliveChildren;

        /// <summary>
        /// Initializes a new <see cref="TreeNode"/> in the <see cref="NodeStatus.OPEN"/> state.
        /// </summary>
        /// <param name="model">The hypothesized Elliott model for this node.</param>
        /// <param name="wavePos">
        /// The wave position this node fills inside its parent (e.g. <c>"1"</c>, <c>"a"</c>);
        /// <c>null</c> for a root node.
        /// </param>
        /// <param name="level">The notation level (0 = smallest, §12).</param>
        /// <param name="childRelation">How this node relates to its children (§6.3/§6.5).</param>
        public TreeNode(
            ElliottModelType model,
            string wavePos,
            byte level,
            ChildRelation childRelation = ChildRelation.SEQUENCE)
        {
            Id = Interlocked.Increment(ref _sNextId);
            Model = model;
            WavePos = wavePos;
            Level = level;
            ChildRelation = childRelation;
        }

        /// <summary>Gets the unique id of the node (used for export, §17).</summary>
        public int Id { get; }

        /// <summary>Gets the hypothesized Elliott model.</summary>
        public ElliottModelType Model { get; }

        /// <summary>Gets the wave position this node fills in its parent (<c>null</c> for root).</summary>
        public string WavePos { get; }

        /// <summary>Gets the notation level (0 = smallest).</summary>
        public byte Level { get; }

        /// <summary>Gets how this node relates to its children.</summary>
        public ChildRelation ChildRelation { get; }

        /// <summary>Gets or sets the inclusive start segment index of the node's range.</summary>
        public int RangeStartSegment { get; set; } = -1;

        /// <summary>Gets or sets the inclusive end segment index of the node's range.</summary>
        public int RangeEndSegment { get; set; } = -1;

        /// <summary>Gets or sets the pivot the node's range starts at.</summary>
        public BarPoint StartPivot { get; set; }

        /// <summary>Gets or sets the pivot the node's range ends at.</summary>
        public BarPoint EndPivot { get; set; }

        /// <summary>Gets or sets the node score (§16).</summary>
        public double Score { get; set; }

        /// <summary>Gets the lifecycle status of the node.</summary>
        public NodeStatus Status { get; private set; } = NodeStatus.OPEN;

        /// <summary>Gets the reason the node died (<see cref="DeathReason.NONE"/> while alive).</summary>
        public DeathReason DeathReason { get; private set; } = DeathReason.NONE;

        /// <summary>Gets the parent node (<c>null</c> for a root).</summary>
        public TreeNode Parent { get; private set; }

        /// <summary>Gets the children of the node.</summary>
        public IReadOnlyList<TreeNode> Children => m_Children;

        /// <summary>Gets the number of children that are not dead.</summary>
        public int AliveChildrenCount => m_AliveChildren;

        /// <summary>Gets a value indicating whether the node is not dead.</summary>
        public bool IsAlive => Status != NodeStatus.DEAD;

        /// <summary>
        /// Attaches <paramref name="child"/> to this node and returns it for chaining.
        /// </summary>
        /// <param name="child">The child to attach (must not already have a parent).</param>
        public TreeNode AddChild(TreeNode child)
        {
            if (child == null)
                throw new ArgumentNullException(nameof(child));
            if (child.Parent != null)
                throw new InvalidOperationException("The child already has a parent.");

            child.Parent = this;
            m_Children.Add(child);
            if (child.IsAlive)
                m_AliveChildren++;

            return child;
        }

        /// <summary>
        /// Marks the node <see cref="NodeStatus.COMPLETE"/> (all expected waves gathered).
        /// </summary>
        public void MarkComplete()
        {
            if (Status == NodeStatus.DEAD)
                throw new InvalidOperationException("A dead node cannot be completed.");
            Status = NodeStatus.COMPLETE;
        }

        /// <summary>
        /// Marks the node <see cref="NodeStatus.PROJECTED"/> (incomplete, reaching the
        /// current bar — §13).
        /// </summary>
        public void MarkProjected()
        {
            if (Status == NodeStatus.DEAD)
                throw new InvalidOperationException("A dead node cannot be projected.");
            Status = NodeStatus.PROJECTED;
        }

        /// <summary>
        /// Kills the node with the given <paramref name="reason"/>. Death cascades down to
        /// every descendant (§6.3) and is reported up to the parent so that backtracking
        /// can propagate when the last alive child of a parent dies (§6.5). Siblings are
        /// never affected.
        /// </summary>
        /// <param name="reason">Why the node died.</param>
        public void Kill(DeathReason reason)
        {
            if (Status == NodeStatus.DEAD)
                return;

            // Resolve own state first so callbacks from cascaded children are ignored.
            Status = NodeStatus.DEAD;
            DeathReason = reason;

            // Cascade down: every still-alive descendant dies too.
            foreach (TreeNode child in m_Children)
            {
                if (child.Status != NodeStatus.DEAD)
                    child.Kill(DeathReason.PARENT_DEAD);
            }

            // Propagate up: let the parent re-evaluate its surviving children.
            Parent?.OnChildDied();
        }

        /// <summary>
        /// Re-evaluates this (parent) node after one of its children died. Implements the
        /// per-relation backtracking of §6.5 without traversing the whole tree.
        /// </summary>
        private void OnChildDied()
        {
            // If already resolved (e.g. we are the node currently being killed), ignore.
            if (Status == NodeStatus.DEAD)
                return;

            if (m_AliveChildren > 0)
                m_AliveChildren--;

            switch (ChildRelation)
            {
                case ChildRelation.SEQUENCE:
                    // Every sub-wave is mandatory: one dead child invalidates the node.
                    Kill(DeathReason.CHILD_INVALID);
                    break;

                case ChildRelation.ALTERNATIVES:
                    // The node survives while any alternative remains.
                    if (m_AliveChildren == 0 && m_Children.Count > 0)
                        Kill(DeathReason.NO_VALID_SUBMODEL);
                    break;
            }
        }

        /// <inheritdoc/>
        public override string ToString() =>
            $"#{Id} {Model}" +
            (WavePos == null ? string.Empty : $".{WavePos}") +
            $" L{Level} [{RangeStartSegment}..{RangeEndSegment}] {Status}" +
            (DeathReason == DeathReason.NONE ? string.Empty : $"({DeathReason})");
    }

    public partial class ElliottWaveExactMarkupV2
    {
        /// <summary>
        /// The root models that may start the markup (see EW_MARKUP_v2.md §5.2). The first
        /// zigzag segment is an impulsive move, so a root can only be a model that begins
        /// with an impulsive sub-model (wave 1 / wave a).
        /// </summary>
        public static readonly IReadOnlyList<ElliottModelType> StartModels = new[]
        {
            ElliottModelType.IMPULSE,
            ElliottModelType.DIAGONAL_CONTRACTING_INITIAL,
            ElliottModelType.ZIGZAG,
            ElliottModelType.DOUBLE_ZIGZAG
        };
    }
}
