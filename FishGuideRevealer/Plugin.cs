using System;
using System.Collections.Generic;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FishGuideRevealer;

public sealed unsafe class Plugin : IDalamudPlugin
{
    private const string AddonName = "FishGuide2";
    private const byte UncaughtAlpha = 90;

    private readonly IFramework framework;
    private readonly IClientState clientState;
    private readonly IGameGui gameGui;

    private bool isPatched;
    private byte[]? fishBitmaskBackup;
    private byte[]? spearfishBitmaskBackup;

    public Plugin(
        IFramework framework,
        IClientState clientState,
        IGameGui gameGui)
    {
        this.framework = framework;
        this.clientState = clientState;
        this.gameGui = gameGui;

        this.framework.Update += this.OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!this.clientState.IsLoggedIn)
        {
            this.isPatched = false;
            this.fishBitmaskBackup = null;
            this.spearfishBitmaskBackup = null;
            return;
        }

        if (!this.isPatched)
        {
            this.ApplyPatch();
        }

        this.UpdateFishGuideVisuals();
    }

    private void ApplyPatch()
    {
        var playerState = PlayerState.Instance();
        if (playerState == null)
            return;

        var fishMask = playerState->CaughtFish;
        if (this.fishBitmaskBackup == null || this.fishBitmaskBackup.Length != fishMask.Length)
            this.fishBitmaskBackup = new byte[fishMask.Length];

        fishMask.CopyTo(this.fishBitmaskBackup);
        fishMask.Fill(0xFF);

        var spearfishMask = playerState->CaughtSpearfish;
        if (this.spearfishBitmaskBackup == null || this.spearfishBitmaskBackup.Length != spearfishMask.Length)
            this.spearfishBitmaskBackup = new byte[spearfishMask.Length];

        spearfishMask.CopyTo(this.spearfishBitmaskBackup);
        spearfishMask.Fill(0xFF);

        this.isPatched = true;
    }

    private void RestorePatch()
    {
        if (!this.isPatched)
            return;

        var playerState = PlayerState.Instance();
        if (playerState != null)
        {
            if (this.fishBitmaskBackup != null)
                this.fishBitmaskBackup.CopyTo(playerState->CaughtFish);

            if (this.spearfishBitmaskBackup != null)
                this.spearfishBitmaskBackup.CopyTo(playerState->CaughtSpearfish);
        }

        this.isPatched = false;
    }

    private bool IsActuallyCaught(ushort id, bool isSpearfishing)
    {
        var backup = isSpearfishing ? this.spearfishBitmaskBackup : this.fishBitmaskBackup;
        if (backup == null)
            return true;

        var bitIndex = isSpearfishing && id >= 20000 ? id - 20000 : id;
        var byteIndex = bitIndex >> 3;
        if (byteIndex < 0 || byteIndex >= backup.Length)
            return true;

        return (backup[byteIndex] & (1 << (bitIndex & 7))) != 0;
    }

    private void UpdateFishGuideVisuals()
    {
        var addonPtr = this.gameGui.GetAddonByName(AddonName);
        if (addonPtr.Address == nint.Zero)
            return;

        var addon = (AtkUnitBase*)addonPtr.Address;
        if (!addon->IsVisible)
            return;

        var gridNodes = this.GetGridNodes(addon);
        if (gridNodes == null)
            return;

        var agent = AgentFishGuide.Instance();
        if (agent == null || agent->Data == null || agent->IsSearchTab)
        {
            foreach (var nodeAddr in gridNodes)
            {
                ((AtkResNode*)nodeAddr)->Color.A = 255;
            }
            return;
        }

        var mode = agent->Mode;
        if (mode > 1)
            return;

        var isSpearfishing = mode == 1;
        var pageIndex = agent->TabSelections[mode].PageIndex;
        var filteredEntries = agent->Data->FilteredEntries;
        var totalCount = (int)filteredEntries.LongCount;

        for (var slotIndex = 0; slotIndex < gridNodes.Count; slotIndex++)
        {
            var node = (AtkResNode*)gridNodes[slotIndex];
            var entryIndex = pageIndex * 100 + slotIndex;
            if (entryIndex >= totalCount)
            {
                node->Color.A = 255;
                continue;
            }

            var entry = filteredEntries[entryIndex].Value;
            if (entry == null)
            {
                node->Color.A = 255;
                continue;
            }

            node->Color.A = this.IsActuallyCaught(entry->Id, isSpearfishing) ? (byte)255 : UncaughtAlpha;
        }
    }

    private List<nint>? GetGridNodes(AtkUnitBase* addon)
    {
        var nodeList = addon->UldManager.NodeList;
        var nodeCount = addon->UldManager.NodeListCount;
        if (nodeList == null || nodeCount == 0)
            return null;

        var groups = new Dictionary<nint, List<nint>>();

        for (var i = 0; i < nodeCount; i++)
        {
            var node = nodeList[i];
            if (node == null || (int)node->Type < 1000 || node->ParentNode == null)
                continue;

            var parentAddr = (nint)node->ParentNode;
            if (!groups.TryGetValue(parentAddr, out var list))
            {
                list = new List<nint>();
                groups[parentAddr] = list;
            }

            list.Add((nint)node);
        }

        foreach (var list in groups.Values)
        {
            if (list.Count == 100)
            {
                list.Sort((a, b) =>
                {
                    var nodeA = (AtkResNode*)a;
                    var nodeB = (AtkResNode*)b;
                    var yCmp = ((int)Math.Round(nodeA->Y / 10f)).CompareTo((int)Math.Round(nodeB->Y / 10f));
                    return yCmp != 0 ? yCmp : ((int)Math.Round(nodeA->X / 10f)).CompareTo((int)Math.Round(nodeB->X / 10f));
                });
                return list;
            }
        }

        return null;
    }

    public void Dispose()
    {
        this.framework.Update -= this.OnFrameworkUpdate;
        this.RestorePatch();
    }
}