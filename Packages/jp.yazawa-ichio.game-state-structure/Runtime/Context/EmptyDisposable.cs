using System;

namespace GameStateStructure
{
	sealed class EmptyDisposable : IDisposable
	{
		public static readonly EmptyDisposable Instance = new EmptyDisposable();

		EmptyDisposable() { }

		public void Dispose() { }
	}
}