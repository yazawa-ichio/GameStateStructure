using System;

namespace GameStateStructure
{
	/// <summary>占有中の車線が BranchOccupied.Reject で要求を拒否した。遷移ロックは停止しない。</summary>
	public class BranchRejectedException : Exception
	{
		public Type Branch { get; }

		public BranchRejectedException(Type branch)
			: base($"branch is occupied : {branch?.Name}")
		{
			Branch = branch;
		}
	}
}