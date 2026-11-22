using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;
using VoidStorage.Data;

namespace VoidStorageBridge.Players;

/// <summary>
/// Feeds VoidStorage contents into vanilla crafting (work benches, anvils, ...) the same way
/// ImproveGame's ExtremeStorage core does: expose clones, then withdraw the consumed difference.
/// </summary>
public class VaultMaterialPlayer : ModPlayer
{
	private int[] _idxType = Array.Empty<int>();
	private int[] _idxPrefix = Array.Empty<int>();
	private int[] _idxRemaining = Array.Empty<int>();

	private static bool? _internalConsumer;

	/// <summary>
	/// Modified (recompiled) VoidStorage builds ship their own StorageMaterialConsumer which already
	/// feeds the vault into vanilla crafting. Skip ours in that case so materials are never counted twice.
	/// </summary>
	private static bool HasInternalConsumer()
	{
		if (_internalConsumer == null)
		{
			try
			{
				_internalConsumer = ModLoader.GetMod("VoidStorage")?.Code?.GetType("VoidStorage.Features.StorageMaterialConsumer") != null;
			}
			catch
			{
				_internalConsumer = false;
			}
		}
		return _internalConsumer.Value;
	}

	public override IEnumerable<Item> AddMaterialsForCrafting(out ItemConsumedCallback itemConsumedCallback)
	{
		itemConsumedCallback = null;
		if (!ModLoader.HasMod("VoidStorage") || HasInternalConsumer())
		{
			return Array.Empty<Item>();
		}

		// NetworkStorage.Loaded is intentionally NOT checked: on multiplayer clients the storage path
		// stays null (data arrives through network sync), yet the dictionaries are fully populated.
		List<NetworkStorage.StorageEntry> entries = NetworkStorage.GetItems(NetworkStorage.StorageSort.TypeId);
		if (entries == null || entries.Count == 0)
		{
			return Array.Empty<Item>();
		}

		_idxType = new int[entries.Count];
		_idxPrefix = new int[entries.Count];
		_idxRemaining = new int[entries.Count];
		List<Item> items = new List<Item>(entries.Count);

		for (int i = 0; i < entries.Count; i++)
		{
			NetworkStorage.StorageEntry entry = entries[i];
			int stack = entry.Count;
			if (stack > 9999)
			{
				stack = 9999;
			}
			if (stack <= 0)
			{
				continue;
			}
			Item item = NetworkStorage.Restore(entry.Type, stack, entry.Prefix);
			if (item == null || item.IsAir)
			{
				continue;
			}
			int idx = items.Count;
			_idxType[idx] = entry.Type;
			_idxPrefix[idx] = entry.Prefix;
			_idxRemaining[idx] = stack;
			items.Add(item);
		}

		itemConsumedCallback = (Item item, int index) =>
		{
			if (index < 0 || index >= items.Count)
			{
				return;
			}
			int consumed = _idxRemaining[index] - item.stack;
			if (consumed <= 0)
			{
				return;
			}
			_idxRemaining[index] = item.stack;
			int type = _idxType[index];
			int prefix = _idxPrefix[index];
			if (prefix != 0)
			{
				NetworkStorage.Withdraw(type, consumed, prefix);
			}
			else
			{
				NetworkStorage.Withdraw(type, consumed);
			}
		};

		return items;
	}
}
