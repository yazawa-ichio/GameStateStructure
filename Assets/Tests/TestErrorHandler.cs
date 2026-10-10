using System;

namespace GameStateStructure.Tests
{
	class TestErrorHandler : IErrorHandler
	{
		public Exception Error { get; private set; }

		public void Handle(Exception error)
		{
			Error = error;
		}

		public void Clear()
		{
			Error = null;
		}
	}


}
