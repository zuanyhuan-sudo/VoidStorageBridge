using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.ModLoader;

namespace VoidStorageBridge.Common;

/// <summary>
/// Read-only/consume bridge to ImproveGame's Super Vault (大背包).
/// ImproveGame exposes it through Mod.Call("GetBigBagItems", player) returning live item references,
/// so stack changes made here are synced automatically by ImproveGame's client-authoritative packets.
/// </summary>
public static class BigBagContents
{
	private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

	private static readonly Dictionary<Type, FieldInfo> _gateCache = new Dictionary<Type, FieldInfo>();

	private static FieldInfo _modPlayersField;

	private static List<Item> GetItems(Player player)
	{
		if (!ModLoader.HasMod("ImproveGame") || player == null)
		{
			return null;
		}
		try
		{
			return ModLoader.GetMod("ImproveGame").Call("GetBigBagItems", player) as List<Item>;
		}
		catch
		{
			return null;
		}
	}

	private static ModPlayer[] ModPlayersOf(Player player)
	{
		if (player == null)
		{
			return null;
		}
		try
		{
			if (_modPlayersField == null)
			{
				_modPlayersField = typeof(Player).GetField("modPlayers", Flags);
			}
			return _modPlayersField?.GetValue(player) as ModPlayer[];
		}
		catch
		{
			return null;
		}
	}

	/// <summary>Honors ImproveGame's "participate in crafting" setting; defaults to true when unknown.</summary>
	internal static bool Participates(Player player)
	{
		ModPlayer[] modPlayers = ModPlayersOf(player);
		if (modPlayers == null)
		{
			return true;
		}
		try
		{
			foreach (ModPlayer mp in modPlayers)
			{
				if (mp == null)
				{
					continue;
				}
				Type type = mp.GetType();
				if (!type.Name.Contains("UIPlayerSetting"))
				{
					continue;
				}
				if (!_gateCache.TryGetValue(type, out FieldInfo field))
				{
					field = type.GetField("SuperVault_ParticipateSynthesis", Flags);
					_gateCache[type] = field;
				}
				if (field != null && field.FieldType == typeof(bool))
				{
					return (bool)field.GetValue(mp);
				}
				return true;
			}
		}
		catch
		{
		}
		return true;
	}

	internal static IEnumerable<Item> Enumerate(Player player)
	{
		if (!Participates(player))
		{
			yield break;
		}
		List<Item> list = GetItems(player);
		if (list == null)
		{
			yield break;
		}
		foreach (Item item in list)
		{
			if (item != null && !item.IsAir)
			{
				yield return item;
			}
		}
	}

	public static int Count(Player player, int type)
	{
		if (!Participates(player))
		{
			return 0;
		}
		int num = 0;
		foreach (Item item in Enumerate(player))
		{
			if (item.type == type)
			{
				num += item.stack;
			}
		}
		return num;
	}

	public static int Consume(Player player, int type, int count)
	{
		if (count <= 0 || !Participates(player))
		{
			return 0;
		}
		List<Item> list = GetItems(player);
		if (list == null)
		{
			return 0;
		}
		int num = count;
		for (int i = list.Count - 1; i >= 0 && num > 0; i--)
		{
			Item item = list[i];
			if (item != null && !item.IsAir && item.type == type)
			{
				int take = Math.Min(item.stack, num);
				item.stack -= take;
				num -= take;
				if (item.stack <= 0)
				{
					item.TurnToAir(false);
				}
			}
		}
		return count - num;
	}

	/// <summary>IL helper: merges the big bag into a crafting pool (returns the same dictionary for stack flow).</summary>
	public static Dictionary<int, int> MergeInto(Dictionary<int, int> pool)
	{
		if (pool == null)
		{
			return pool;
		}
		foreach (Item item in Enumerate(Main.LocalPlayer))
		{
			pool[item.type] = pool.GetValueOrDefault(item.type) + item.stack;
		}
		return pool;
	}

	/// <summary>IL helper: fingerprint of the big bag so VoidStorage's craftable cache refreshes.</summary>
	public static int Footprint(Player player)
	{
		int num = 0;
		foreach (Item item in Enumerate(player))
		{
			num = num * 31 + item.type;
			num = num * 31 + item.stack;
		}
		return num;
	}
}
