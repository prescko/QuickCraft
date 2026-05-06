using System.Reflection;
using Vintagestory.API.Client;

namespace QuickCraft;

internal static class InventoryPacketPatcher
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Send(ICoreClientAPI api, object? packet)
    {
        if (packet == null)
        {
            return;
        }

        if (packet is Array packets)
        {
            foreach (object? entry in packets)
            {
                Send(api, entry);
            }

            return;
        }

        PatchLastChanged(packet);
        api.Network.SendPacketClient(packet);
    }

    public static void PatchLastChanged(object? packet)
    {
        if (packet == null)
        {
            return;
        }

        if (packet is Array packets)
        {
            foreach (object? entry in packets)
            {
                PatchLastChanged(entry);
            }

            return;
        }

        PatchPacket(packet);
        PatchPacket(ReadMember(packet, "MoveItemstack"));
        PatchPacket(ReadMember(packet, "ActivateInventorySlot"));
        PatchPacket(ReadMember(packet, "Flipitemstacks"));
        PatchPacket(ReadMember(packet, "CreateItemstack"));
    }

    private static void PatchPacket(object? packet)
    {
        if (packet == null)
        {
            return;
        }

        if (packet.GetType().Name == "Packet_ActivateInventorySlot")
        {
            SetMember(packet, "TargetLastChanged", 0);
            return;
        }

        SetMember(packet, "SourceLastChanged", long.MaxValue);
        SetMember(packet, "TargetLastChanged", long.MaxValue);
    }

    private static object? ReadMember(object instance, string name)
    {
        Type type = instance.GetType();
        return type.GetField(name, Members)?.GetValue(instance)
            ?? type.GetProperty(name, Members)?.GetValue(instance);
    }

    private static void SetMember(object instance, string name, long value)
    {
        Type type = instance.GetType();
        FieldInfo? field = type.GetField(name, Members);
        if (field != null && field.FieldType == typeof(long))
        {
            field.SetValue(instance, value);
            return;
        }

        PropertyInfo? property = type.GetProperty(name, Members);
        if (property?.CanWrite == true && property.PropertyType == typeof(long))
        {
            property.SetValue(instance, value);
        }
    }
}
