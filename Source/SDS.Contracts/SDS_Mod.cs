using RimWorld;
using Verse;

namespace SuperDimensionalStorage
{
    /// <summary>Mod 设置：容量与耗电均可自定义，0 表示不耗电。</summary>
    public class SDS_Settings : ModSettings
    {
        public int gridCapacity = SDS_Config.GridCapacityDefault;
        public int capacityPerArray = SDS_Config.CapacityPerArrayDefault;
        public float corePower = SDS_Config.CorePowerDefault;
        public float arrayPower = SDS_Config.ArrayPowerDefault;
        public float portPower = SDS_Config.PortPowerDefault;
        public bool excludeGridFromWealth;
        public bool playerFactionOnlyAccess;
        public bool allowPriorityOutflow;
        public bool highestStoragePriority;
        public bool forceWeaponApparelVirtual;
        public int endpointStoragePriority = (int)StoragePriority.Normal;
        public bool separateByQuality = true;
        public bool separateByStuff = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref gridCapacity, "gridCapacity", SDS_Config.GridCapacityDefault);
            Scribe_Values.Look(ref capacityPerArray, "capacityPerArray", SDS_Config.CapacityPerArrayDefault);
            Scribe_Values.Look(ref corePower, "corePower", SDS_Config.CorePowerDefault);
            Scribe_Values.Look(ref arrayPower, "arrayPower", SDS_Config.ArrayPowerDefault);
            Scribe_Values.Look(ref portPower, "portPower", SDS_Config.PortPowerDefault);
            Scribe_Values.Look(ref excludeGridFromWealth, "excludeGridFromWealth", false);
            Scribe_Values.Look(ref playerFactionOnlyAccess, "playerFactionOnlyAccess", false);
            Scribe_Values.Look(ref allowPriorityOutflow, "allowPriorityOutflow", false);
            Scribe_Values.Look(ref highestStoragePriority, "highestStoragePriority", false);
            Scribe_Values.Look(ref forceWeaponApparelVirtual, "forceWeaponApparelVirtual", false);
            Scribe_Values.Look(ref endpointStoragePriority, "endpointStoragePriority", (int)StoragePriority.Normal);
            Scribe_Values.Look(ref separateByQuality, "separateByQuality", true);
            Scribe_Values.Look(ref separateByStuff, "separateByStuff", true);
        }

        /// <summary>恢复默认值；生效后的通知与应用由 Bootstrap 负责。</summary>
        public void ResetToDefaults()
        {
            gridCapacity = SDS_Config.GridCapacityDefault;
            capacityPerArray = SDS_Config.CapacityPerArrayDefault;
            corePower = SDS_Config.CorePowerDefault;
            arrayPower = SDS_Config.ArrayPowerDefault;
            portPower = SDS_Config.PortPowerDefault;
            excludeGridFromWealth = false;
            playerFactionOnlyAccess = false;
            allowPriorityOutflow = false;
            highestStoragePriority = false;
            forceWeaponApparelVirtual = false;
            endpointStoragePriority = (int)StoragePriority.Normal;
            separateByQuality = true;
            separateByStuff = true;
            ClampAll();
        }

        /// <summary>把所有取值钳制到规定范围内。</summary>
        public void ClampAll()
        {
            gridCapacity = SDS_Config.ClampGridCapacity(gridCapacity);
            capacityPerArray = SDS_Config.ClampCapacityPerArray(capacityPerArray);
            corePower = SDS_Config.Clamp(corePower, SDS_Config.CorePowerMin, SDS_Config.CorePowerMax);
            arrayPower = SDS_Config.Clamp(arrayPower, SDS_Config.ArrayPowerMin, SDS_Config.ArrayPowerMax);
            portPower = SDS_Config.Clamp(portPower, SDS_Config.PortPowerMin, SDS_Config.PortPowerMax);
            endpointStoragePriority = SDS_Config.Clamp(endpointStoragePriority,
                (int)StoragePriority.Low, (int)StoragePriority.Critical);
            // W52：两种优先级模式互斥；配置异常时以「最高优先级」优先，保守地不流出。
            if (allowPriorityOutflow && highestStoragePriority)
            {
                allowPriorityOutflow = false;
            }
        }
    }

    /// <summary>数值默认值与取值范围；运行时实际取值由 Bootstrap 持有的设置实例提供。</summary>
    public static class SDS_Config
    {
        public const int GridCapacityDefault = 30;
        public const int GridCapacityMin = 10;
        public const int GridCapacityMax = 90;

        public const int CapacityPerArrayDefault = 30;
        public const int CapacityPerArrayMin = 10;
        public const int CapacityPerArrayMax = 90;

        public const float CorePowerDefault = 1000f;
        public const float CorePowerMin = 0f;
        public const float CorePowerMax = 2000f;

        public const float ArrayPowerDefault = 1000f;
        public const float ArrayPowerMin = 0f;
        public const float ArrayPowerMax = 2000f;

        public const float PortPowerDefault = 10f;
        public const float PortPowerMin = 0f;
        public const float PortPowerMax = 100f;

        public const string CoreDefName = "SDS_BuildingCore";
        public const string ArrayDefName = "SDS_BuildingCapacityArray";
        public const string PortDefName = "SDS_BuildingPort";

        /// <summary>运行期设置实例：Bootstrap 启动时写入一次；Core / Vanilla 只读。</summary>
        public static SDS_Settings Current;

        public static int GridCapacity
        {
            get { return Current != null ? Current.gridCapacity : GridCapacityDefault; }
        }

        public static int CapacityPerArray
        {
            get { return Current != null ? Current.capacityPerArray : CapacityPerArrayDefault; }
        }

        public static bool SeparateByQuality
        {
            get { return Current == null || Current.separateByQuality; }
        }

        public static bool SeparateByStuff
        {
            get { return Current == null || Current.separateByStuff; }
        }

        public static bool IgnorePriorityMode
        {
            get { return Current != null && Current.highestStoragePriority; }
        }

        public static bool ForceWeaponApparelVirtual
        {
            get { return Current != null && Current.forceWeaponApparelVirtual; }
        }

        public static bool ExcludeGridFromWealth
        {
            get { return Current != null && Current.excludeGridFromWealth; }
        }

        /// <summary>开启后只有玩家派系可以取用超维库存；关闭时保持原版 AI 口径。</summary>
        public static bool PlayerFactionOnlyAccess
        {
            get { return Current != null && Current.playerFactionOnlyAccess; }
        }

        /// <summary>W52：允许镜像按原版优先级流出；与「最高优先级」互斥。</summary>
        public static bool AllowPriorityOutflow
        {
            get
            {
                return Current != null && Current.allowPriorityOutflow
                    && !Current.highestStoragePriority;
            }
        }

        public static StoragePriority CoreStoragePriority
        {
            get
            {
                int value = Current != null
                    ? Current.endpointStoragePriority
                    : (int)StoragePriority.Normal;
                return (StoragePriority)Clamp(value,
                    (int)StoragePriority.Low, (int)StoragePriority.Critical);
            }
        }

        public static int ClampGridCapacity(int value)
        {
            return Clamp(value, GridCapacityMin, GridCapacityMax);
        }

        public static int ClampCapacityPerArray(int value)
        {
            return Clamp(value, CapacityPerArrayMin, CapacityPerArrayMax);
        }

        public static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }
            return value > max ? max : value;
        }

        public static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }
            return value > max ? max : value;
        }
    }
}
