using System;

namespace GameStateStructure.Tests
{
	class TestErrorHandler : IErrorHandler
	{
		public static Exception Error;

		public void Handle(Exception error)
		{
			Error = error;
		}
	}


}
