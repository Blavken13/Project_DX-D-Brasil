using MsgPack;

namespace Messages;

public struct EntityKey
{
	public string EntityId;

	public static void Pack(Packer packer, EntityKey val, bool hint = false)
	{
		packer.PackArrayHeader(1);
		if (val.EntityId == null)
		{
			packer.PackString(string.Empty);
		}
		else
		{
			packer.PackString(val.EntityId);
		}
	}

	public static EntityKey Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new EntityKey
		{
			EntityId = unpacker.LastReadData.AsString()
		};
	}

	public override string ToString()
	{
		return "<EntityKey EntityId=" + EntityId + ">";
	}
}
