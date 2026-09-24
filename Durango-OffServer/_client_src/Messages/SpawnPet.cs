using MsgPack;

namespace Messages;

public struct SpawnPet
{
	public const uint TypeCode = 923570u;

	public string PetId;

	public static void Pack(Packer packer, SpawnPet val, bool hint = false)
	{
		if (hint)
		{
			packer.PackArrayHeader(2);
			packer.Pack(923570u);
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

	public static SpawnPet Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new SpawnPet
		{
			PetId = unpacker.LastReadData.AsString()
		};
	}

	public override string ToString()
	{
		return "<SpawnPet PetId=" + PetId + ">";
	}
}
