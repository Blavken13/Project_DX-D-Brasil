using MsgPack;

namespace Messages;

public struct RevertPetRank
{
	public const uint TypeCode = 74014u;

	public string PetId;

	public static void Pack(Packer packer, RevertPetRank val, bool hint = false)
	{
		if (hint)
		{
			packer.PackArrayHeader(2);
			packer.Pack(74014u);
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

	public static RevertPetRank Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new RevertPetRank
		{
			PetId = unpacker.LastReadData.AsString()
		};
	}

	public override string ToString()
	{
		return "<RevertPetRank PetId=" + PetId + ">";
	}
}
