using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// 统一端口句柄（准则一 / 推论三）：建筑、小人、机械体共用同一个句柄。
    /// 端点的物理形态只影响"站位格 / 寻路方式"；可用性 / 接收 / 存入口径完全一致。
    /// </summary>
    internal sealed class SDS_EndpointHandle : ISDS_Port
    {
        public Thing Thing { get; private set; }

        public Pawn Pawn { get; private set; }

        public bool IsSelf { get; private set; }

        private ISDS_StorageEndpoint BuildingPort
        {
            get { return Thing as ISDS_StorageEndpoint; }
        }

        public Map Map
        {
            get
            {
                if (Pawn != null)
                {
                    return Pawn.Spawned ? Pawn.Map : null;
                }
                return Thing != null && Thing.Spawned ? Thing.Map : null;
            }
        }

        public bool Operational
        {
            get
            {
                if (Pawn != null)
                {
                    return Pawn.Spawned && !Pawn.Dead && SDS_ImplantUtility.IsPortOperational(Pawn);
                }
                ISDS_StorageEndpoint port = BuildingPort;
                return port != null && port.Operational;
            }
        }

        public IntVec3 AccessCell
        {
            get { return AccessCellOf(Thing); }
        }

        public PathEndMode AccessMode
        {
            get
            {
                if (Pawn != null)
                {
                    return PathEndMode.Touch;
                }
                Building building = Thing as Building;
                if (building != null && building.Spawned && building.Map != null
                    && building.def != null && building.def.hasInteractionCell)
                {
                    IntVec3 cell = building.InteractionCell;
                    if (cell.IsValid && cell.InBounds(building.Map) && cell.Walkable(building.Map))
                    {
                        return PathEndMode.InteractionCell;
                    }
                }
                return PathEndMode.Touch;
            }
        }

        public bool CanReach(Pawn actor)
        {
            if (actor == null || Thing == null)
            {
                return false;
            }
            if (IsSelf)
            {
                return true;
            }
            Danger danger = actor.NormalMaxDanger();
            if (Pawn != null)
            {
                return actor.CanReach(Pawn, PathEndMode.Touch, danger);
            }
            return actor.CanReach(Thing, AccessMode, danger);
        }

        public float RankDistanceSquared(IntVec3 reference)
        {
            // 自身端口按真实位置参与排序；折叠为自身由 W62 在端点胜出后完成。
            return DistanceSquared(reference, Thing);
        }

        // ---------- 构造 ----------

        public static SDS_EndpointHandle ForBuilding(ISDS_StorageEndpoint endpoint)
        {
            Thing thing = endpoint as Thing;
            if (thing == null)
            {
                return null;
            }
            return new SDS_EndpointHandle { Thing = thing };
        }

        public static SDS_EndpointHandle ForPawn(Pawn pawn, bool self)
        {
            if (pawn == null)
            {
                return null;
            }
            return new SDS_EndpointHandle { Thing = pawn, Pawn = pawn, IsSelf = self };
        }

        public static bool TryFromThing(Thing thing, out SDS_EndpointHandle handle)
        {
            handle = null;
            if (thing == null)
            {
                return false;
            }
            ISDS_StorageEndpoint endpoint = thing as ISDS_StorageEndpoint;
            if (endpoint != null)
            {
                handle = ForBuilding(endpoint);
                return handle != null;
            }
            Pawn pawn = thing as Pawn;
            if (pawn != null && SDS_ImplantUtility.IsPortOperational(pawn))
            {
                handle = ForPawn(pawn, false);
                return true;
            }
            return false;
        }

        // ---------- 统一的站位 / 距离口径 ----------

        /// <summary>
        /// 基础站位格（距离 / 对齐 / W63 落点共用口径）：小人 = 所在格；建筑 = 交互格（不可站时退回建筑格）。
        /// 只作参考点，不保证可走；落点是否可达由调用方按同一 reachability 判定。
        /// </summary>
        public static IntVec3 AccessCellOf(Thing thing)
        {
            if (thing == null)
            {
                return IntVec3.Invalid;
            }
            Pawn pawn = thing as Pawn;
            if (pawn != null)
            {
                return pawn.Spawned ? pawn.Position : IntVec3.Invalid;
            }
            Building building = thing as Building;
            if (building != null && building.Spawned && building.Map != null)
            {
                IntVec3 cell = building.InteractionCell;
                if (cell.IsValid && cell.InBounds(building.Map) && cell.Walkable(building.Map))
                {
                    return cell;
                }
                return building.Position;
            }
            return thing.PositionHeld;
        }

        public static float DistanceSquared(IntVec3 reference, Thing thing)
        {
            return DistanceSquared(reference, AccessCellOf(thing));
        }

        public static float DistanceSquared(IntVec3 reference, IntVec3 cell)
        {
            return reference.IsValid && cell.IsValid
                ? (reference - cell).LengthHorizontalSquared
                : float.MaxValue;
        }

        // ---------- 接收 / 存入（唯一硬门在 SDS_EndpointUtility） ----------

        public bool Accepts(Thing thing)
        {
            return Accepts(thing, false);
        }

        public bool Accepts(Thing thing, bool allowProducedMarked)
        {
            return SDS_EndpointUtility.Accepts(this, thing, allowProducedMarked);
        }

        public bool TryDeposit(Thing thing, out int accepted)
        {
            return TryDeposit(thing, out accepted, false);
        }

        public bool TryDeposit(Thing thing, out int accepted, bool allowProducedMarked)
        {
            accepted = 0;
            if (!Accepts(thing, allowProducedMarked))
            {
                return false;
            }
            return SDS_Rules.TryDeposit(thing, this, out accepted) && accepted > 0;
        }

        /// <summary>从端点取出（W2 自测 / 后续 JobDriver 共用）：只经 SDS_Core 的事务与账本。</summary>
        public bool TryWithdraw(SDS_StockKey key, int count, Pawn actor, out List<Thing> made)
        {
            made = null;
            if (key == null || key.preserved || count <= 0 || !Operational || !SDS_Rules.Available)
            {
                return false;
            }
            // S18：非玩家派系不允许通过端点取出。
            if (SDS_AccessRules.BlocksNonPlayer(actor))
            {
                return false;
            }
            SDS_WithdrawPlan plan = new SDS_WithdrawPlan
            {
                query = new SDS_WithdrawQuery
                {
                    key = key,
                    quantity = count
                },
                endpoint = this,
                mode = SDS_WithdrawMode.Hand
            };
            return SDS_Rules.TryWithdraw(plan, actor, out made);
        }
    }
}
