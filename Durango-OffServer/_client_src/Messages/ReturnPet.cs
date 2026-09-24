using MsgPack;

namespace Messages;

public struct ReturnPet
{
	public const uint TypeCode = 808u;

	public string PetId;

	public static void Pack(Packer packer, ReturnPet val, bool hint = false)
	{
		if (hint)
		{
			packer.PackArrayHeader(2);
			packer.Pack(808u);
		}
		else
		{
			packer.PackArrayHeader(1);
		}
		if (val.PetId == null)
		{
			packer.PackString(string.Empty);
		}
		else
		{
			packer.PackString(val.PetId);
		}
	}

	public static ReturnPet Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new ReturnPet
		{
			PetId = unpacker.LastReadData.AsString()
		};
	}

	public override string ToString()
	{
		return "<ReturnPet PetId=" + PetId + ">";
	}
}
