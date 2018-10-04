using System;

namespace DynamicExpresso.Parsing
{
	internal class ParseSignatures
	{
		public interface ILogicalSignatures
		{
			void F(bool x, bool y);
			void F(bool? x, bool? y);
		}

		public interface IArithmeticSignatures
		{
			void F(int x, int y);
			void F(uint x, uint y);
			void F(long x, long y);
			void F(ulong x, ulong y);
			void F(float x, float y);
			void F(double x, double y);
			void F(decimal x, decimal y);
			void F(int? x, int? y);
			void F(uint? x, uint? y);
			void F(long? x, long? y);
			void F(ulong? x, ulong? y);
			void F(float? x, float? y);
			void F(double? x, double? y);
			void F(decimal? x, decimal? y);
		}

		public interface IRelationalSignatures : IArithmeticSignatures
		{
			void F(string x, string y);
			void F(char x, char y);
			void F(DateTime x, DateTime y);
			void F(TimeSpan x, TimeSpan y);
			void F(char? x, char? y);
			void F(DateTime? x, DateTime? y);
			void F(TimeSpan? x, TimeSpan? y);
		}

		public interface IEqualitySignatures : IRelationalSignatures
		{
			void F(bool x, bool y);
			void F(bool? x, bool? y);
		}

		public interface IAddSignatures : IArithmeticSignatures
		{
			void F(DateTime x, TimeSpan y);
			void F(TimeSpan x, TimeSpan y);
			void F(DateTime? x, TimeSpan? y);
			void F(TimeSpan? x, TimeSpan? y);
		}

		public interface ISubtractSignatures : IAddSignatures
		{
			void F(DateTime x, DateTime y);
			void F(DateTime? x, DateTime? y);
		}

		public interface INegationSignatures
		{
			void F(int x);
			void F(long x);
			void F(float x);
			void F(double x);
			void F(decimal x);
			void F(int? x);
			void F(long? x);
			void F(float? x);
			void F(double? x);
			void F(decimal? x);
		}

		public interface INotSignatures
		{
			void F(bool x);
			void F(bool? x);
		}
	}
}
