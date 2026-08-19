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

	/// <summary>
	/// Which instruments step 6 reads to decide whether the leaders are participating.
	/// </summary>
	public enum BreadthSourceMode
	{
		/// <summary>The named leader symbols, counted individually. Needs equity data, which this platform's feed does not carry - so in practice it needs files.</summary>
		Leaders,

		/// <summary>The spread between the traded index and a broader one, priced continuously. Futures on the same feed, so a backtest and live behave alike.</summary>
		RelativeStrength
	}
}