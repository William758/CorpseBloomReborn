using UnityEngine.Networking;

namespace TPDespair.CorpseBloomReborn
{
	public class ReserveAmountMessage : MessageBase
	{
		public float currentReserve;
		public float maxReserve;



		public ReserveAmountMessage(float current, float max)
		{
			currentReserve = current;
			maxReserve = max;
		}



		public override void Serialize(NetworkWriter writer)
		{
			writer.Write(currentReserve);
			writer.Write(maxReserve);
		}

		public override void Deserialize(NetworkReader reader)
		{
			currentReserve = reader.ReadSingle();
			maxReserve = reader.ReadSingle();
		}
	}



	public class ReserveTargetMessage : MessageBase
	{
		public NetworkInstanceId netId;



		public ReserveTargetMessage(NetworkInstanceId id)
		{
			netId = id;
		}



		public override void Serialize(NetworkWriter writer)
		{
			writer.Write(netId);
		}

		public override void Deserialize(NetworkReader reader)
		{
			netId = reader.ReadNetworkId();
		}
	}
}