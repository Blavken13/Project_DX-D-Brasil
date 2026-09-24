using MsgPack;

namespace Messages;

public struct UserFirstPurchase
{
	public string CommodityId;

	public string PurchaseId;

	public static void Pack(Packer packer, UserFirstPurchase val, bool hint = false)
	{
		packer.PackArrayHeader(2);
		if (val.CommodityId == null)
		{
			packer.PackString(string.Empty);
		}
		else
		{
			packer.PackString(val.CommodityId);
		}
		if (val.PurchaseId == null)
		{
			packer.PackString(string.Empty);
		}
		else
		{
			packer.PackString(val.PurchaseId);
		}
	}

	public static UserFirstPurchase Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		UserFirstPurchase result = new UserFirstPurchase
		{
			CommodityId = unpacker.LastReadData.AsString()
		};
		unpacker.Read();
		result.PurchaseId = unpacker.LastReadData.AsString();
		return result;
	}

	public override string ToString()
	{
		return "<UserFirstPurchase CommodityId=" + CommodityId + " PurchaseId=" + PurchaseId + ">";
	}
}
