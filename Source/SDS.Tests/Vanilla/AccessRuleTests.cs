using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    /// <summary>S18：非玩家取用门的唯一规则函数。</summary>
    public class AccessRuleTests
    {
        [Fact]
        public void BlocksNonPlayer_SettingOff_AllowsForeignPawn()
        {
            WithAccessSetting(false, delegate
            {
                Pawn foreign = PawnWithFaction(ApiTestKit.ForeignFaction());
                Assert.False(SDS_AccessRules.BlocksNonPlayer(foreign));
            });
        }

        [Fact]
        public void BlocksNonPlayer_SettingOn_BlocksForeignPawn()
        {
            WithAccessSetting(true, delegate
            {
                Pawn foreign = PawnWithFaction(ApiTestKit.ForeignFaction());
                Assert.True(SDS_AccessRules.BlocksNonPlayer(foreign));
            });
        }

        [Fact]
        public void BlocksNonPlayer_SettingOn_AllowsPlayerPawn()
        {
            WithAccessSetting(true, delegate
            {
                Map map = ApiTestKit.NewMap();
                try
                {
                    Pawn player = ApiTestKit.PlayerPawn(map);
                    Assert.False(SDS_AccessRules.BlocksNonPlayer(player));
                }
                finally
                {
                    // ApiTestKit.PlayerPawn 会把壳 Map 登记进 Find.Maps；不摘除会污染后续 Thing.Destroy。
                    ApiTestKit.ReleaseMap(map);
                }
            });
        }

        [Fact]
        public void BlocksNonPlayer_SettingOn_AllowsUnknownActor()
        {
            WithAccessSetting(true, delegate
            {
                Assert.False(SDS_AccessRules.BlocksNonPlayer(null));
            });
        }

        private static void WithAccessSetting(bool on, Action action)
        {
            SDS_Settings previous = SDS_Config.Current;
            try
            {
                ApiTestKit.Create();
                SDS_Config.Current.playerFactionOnlyAccess = on;
                action();
            }
            finally
            {
                SDS_Config.Current = previous;
            }
        }

        private static Pawn PawnWithFaction(Faction faction)
        {
            Pawn pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
            FieldInfo field = typeof(Pawn).GetField("factionInt",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException("AccessRuleTests: Pawn.factionInt 字段不存在");
            }
            field.SetValue(pawn, faction);
            return pawn;
        }
    }
}
