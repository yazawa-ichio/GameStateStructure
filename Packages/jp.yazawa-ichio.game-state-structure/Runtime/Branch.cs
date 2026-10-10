using System;
using System.Collections.Generic;

namespace GameStateStructure
{
	/// <summary>車線が占有されているときの方針。</summary>
	public enum BranchOccupied
	{
		Replace,
		Reject,
	}

	/// <summary>並列に走らせる子を入れる車線。派生型そのものが車線の識別子になる。</summary>
	public abstract class Branch
	{
		static readonly Dictionary<Type, Branch> s_Cache = new Dictionary<Type, Branch>();

		/// <summary>占有中に要求が来たときの扱い。</summary>
		public virtual BranchOccupied Occupied => BranchOccupied.Replace;

		internal static Branch Get(Type type)
		{
			if (type == null)
			{
				return null;
			}
			lock (s_Cache)
			{
				if (s_Cache.TryGetValue(type, out var branch))
				{
					return branch;
				}
				return s_Cache[type] = (Branch)System.Activator.CreateInstance(type);
			}
		}
	}
}