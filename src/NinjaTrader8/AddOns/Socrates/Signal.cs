// Socrates NQ - Trade direction
//
// Lives in its own namespace so both the market-analysis engine and the risk layer
// can refer to a direction without either depending on the other.

namespace Socrates.Strategy
{
	public enum TradeDirection
	{
		None,
		Long,
		Short
	}
}
