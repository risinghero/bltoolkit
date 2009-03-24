﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;

using BLToolkit.Mapping;

namespace BLToolkit.Data.Linq
{
	using Sql;

	class ExpressionInfo<T>
	{
		public Expression                     Expression;
		public MappingSchema                  MappingSchema;
		public SqlBuilder                     SqlBuilder;
		public Func<DbManager,IEnumerable<T>> GetIEnumerable;
		public ExpressionInfo<T>              Next;

		#region GetInfo

		static ExpressionInfo<T> _first;
		static object            _sync      = new object();
		const  int               _cacheSize = 100;

		public static ExpressionInfo<T> GetExpressionInfo(MappingSchema mappingSchema, Expression expr)
		{
			var info = FindInfo(mappingSchema, expr);

			if (info == null)
			{
				lock (_sync)
				{
					info = FindInfo(mappingSchema, expr);

					if (info == null)
					{
						info = new ExpressionParser<T>().Parse(mappingSchema, expr);
						info.Next = _first;
						_first = info;
					}
				}
			}

			return info;
		}

		static ExpressionInfo<T> FindInfo( MappingSchema mappingSchema, Expression expr)
		{
			ExpressionInfo<T> prev = null;
			var n = 0;

			for (var info = _first; info != null; info = info.Next)
			{
				if (info.Compare(mappingSchema, expr))
				{
					if (prev != null)
					{
						lock (_sync)
						{
							prev.Next = info.Next;
							info.Next = _first;
							_first    = info;
						}
					}

					return info;
				}

				if (n++ >= _cacheSize)
				{
					info.Next = null;
					return null;
				}

				prev = info;
			}

			return null;
		}

		#endregion

		#region Query

		internal IEnumerable<T> Query(DbManager db, SqlBuilder sql)
		{
			if (db == null)
			{
				using (db = new DbManager())
					using (IDataReader dr = db.SetCommand(sql).ExecuteReader())
						while (dr.Read())
							yield return MappingSchema.MapDataReaderToObject<T>(dr, null);
			}
			else
			{
				using (IDataReader dr = db.SetCommand(sql).ExecuteReader())
					while (dr.Read())
						yield return MappingSchema.MapDataReaderToObject<T>(dr, null);
			}
		}

		#endregion

		#region Compare

		public bool Compare( MappingSchema mappingSchema, Expression expr)
		{
			return MappingSchema == mappingSchema && Compare(expr, Expression);
		}

		static bool Compare(Expression expr1, Expression expr2)
		{
			if (expr1 == expr2)
				return true;

			if (expr1 == null || expr2 == null || expr1.NodeType != expr2.NodeType || expr1.Type != expr2.Type)
				return false;

			switch (expr1.NodeType)
			{
				case ExpressionType.Add:
				case ExpressionType.AddChecked:
				case ExpressionType.And:
				case ExpressionType.AndAlso:
				case ExpressionType.ArrayIndex:
				case ExpressionType.Coalesce:
				case ExpressionType.Divide:
				case ExpressionType.Equal:
				case ExpressionType.ExclusiveOr:
				case ExpressionType.GreaterThan:
				case ExpressionType.GreaterThanOrEqual:
				case ExpressionType.LeftShift:
				case ExpressionType.LessThan:
				case ExpressionType.LessThanOrEqual:
				case ExpressionType.Modulo:
				case ExpressionType.Multiply:
				case ExpressionType.MultiplyChecked:
				case ExpressionType.NotEqual:
				case ExpressionType.Or:
				case ExpressionType.OrElse:
				case ExpressionType.Power:
				case ExpressionType.RightShift:
				case ExpressionType.Subtract:
				case ExpressionType.SubtractChecked:
					{
						var e1 = (BinaryExpression)expr1;
						var e2 = (BinaryExpression)expr2;
						return
							e1.Method == e2.Method &&
							Compare(e1.Conversion, e2.Conversion) &&
							Compare(e1.Left,       e2.Left) &&
							Compare(e1.Right,      e2.Right);
					}

				case ExpressionType.ArrayLength:
				case ExpressionType.Convert:
				case ExpressionType.ConvertChecked:
				case ExpressionType.Negate:
				case ExpressionType.NegateChecked:
				case ExpressionType.Not:
				case ExpressionType.Quote:
				case ExpressionType.TypeAs:
				case ExpressionType.UnaryPlus:
					{
						var e1 = (UnaryExpression)expr1;
						var e2 = (UnaryExpression)expr2;
						return e1.Method == e2.Method && Compare(e1.Operand, e2.Operand);
					}

				case ExpressionType.Call:
					{
						var e1 = (MethodCallExpression)expr1;
						var e2 = (MethodCallExpression)expr2;

						if (e1.Arguments.Count != e2.Arguments.Count || e1.Method != e2.Method || !Compare(e1.Object, e2.Object))
							return false;

						for (var i = 0; i < e1.Arguments.Count; i++)
							if (!Compare(e1.Arguments[i], e2.Arguments[i]))
								return false;

						return true;
					}

				case ExpressionType.Conditional:
					{
						var e1 = (ConditionalExpression)expr1;
						var e2 = (ConditionalExpression)expr2;
						return Compare(e1.Test, e2.Test) && Compare(e1.IfTrue, e2.IfTrue) && Compare(e1.IfFalse, e2.IfFalse);
					}

				case ExpressionType.Constant:
					{
						var e1 = (ConstantExpression)expr1;
						var e2 = (ConstantExpression)expr2;

						return IsConstant(e1.Type)? object.Equals(e1.Value, e2.Value): true;
					}

				case ExpressionType.Invoke:
					{
						var e1 = (InvocationExpression)expr1;
						var e2 = (InvocationExpression)expr2;

						if (e1.Arguments.Count != e2.Arguments.Count || !Compare(e1.Expression, e2.Expression))
							return false;

						for (var i = 0; i < e1.Arguments.Count; i++)
							if (!Compare(e1.Arguments[i], e2.Arguments[i]))
								return false;

						return true;
					}

				case ExpressionType.Lambda:
					{
						var e1 = (LambdaExpression)expr1;
						var e2 = (LambdaExpression)expr2;

						if (e1.Parameters.Count != e2.Parameters.Count || !Compare(e1.Body, e2.Body))
							return false;

						for (var i = 0; i < e1.Parameters.Count; i++)
							if (!Compare(e1.Parameters[i], e2.Parameters[i]))
								return false;

						return true;
					}

				case ExpressionType.ListInit:
					{
						var e1 = (ListInitExpression)expr1;
						var e2 = (ListInitExpression)expr2;

						if (e1.Initializers.Count != e2.Initializers.Count || !Compare(e1.NewExpression, e2.NewExpression))
							return false;

						for (var i = 0; i < e1.Initializers.Count; i++)
						{
							var i1 = e1.Initializers[i];
							var i2 = e2.Initializers[i];

							if (i1.Arguments.Count != i2.Arguments.Count || i1.AddMethod != i2.AddMethod)
								return false;

							for (var j = 0; j < i1.Arguments.Count; j++)
								if (!Compare(i1.Arguments[j], i2.Arguments[j]))
									return false;
						}

						return true;
					}

				case ExpressionType.MemberAccess:
					{
						var e1 = (MemberExpression)expr1;
						var e2 = (MemberExpression)expr2;
						return e1.Member == e2.Member && Compare(e1.Expression, e2.Expression);
					}

				case ExpressionType.MemberInit:
					{
						var e1 = (MemberInitExpression)expr1;
						var e2 = (MemberInitExpression)expr2;

						if (e1.Bindings.Count != e2.Bindings.Count || !Compare(e1.NewExpression, e2.NewExpression))
							return false;

						Func<MemberBinding,MemberBinding,bool> compareBindings = null; compareBindings = (b1,b2) =>
						{
							if (b1 == b2)
								return true;

							if (b1 == null || b2 == null || b1.BindingType != b2.BindingType || b1.Member != b2.Member)
								return false;

							switch (b1.BindingType)
							{
								case MemberBindingType.Assignment:
									return Compare(((MemberAssignment)b1).Expression, ((MemberAssignment)b2).Expression);

								case MemberBindingType.ListBinding:
									var ml1 = (MemberListBinding)b1;
									var ml2 = (MemberListBinding)b2;

									if (ml1.Initializers.Count != ml2.Initializers.Count)
										return false;

									for (int i = 0; i < ml1.Initializers.Count; i++)
									{
										var ei1 = ml1.Initializers[i];
										var ei2 = ml2.Initializers[i];

										if (ei1.AddMethod != ei2.AddMethod || ei1.Arguments.Count != ei2.Arguments.Count)
											return false;

										for (int j = 0; j < ei1.Arguments.Count; j++)
											if (!Compare(ei1.Arguments[j], ei2.Arguments[j]))
												return false;
									}

									break;

								case MemberBindingType.MemberBinding:
									var mm1 = (MemberMemberBinding)b1;
									var mm2 = (MemberMemberBinding)b2;

									if (mm1.Bindings.Count != mm2.Bindings.Count)
										return false;

									for (int i = 0; i < mm1.Bindings.Count; i++)
										if (!compareBindings(mm1.Bindings[i], mm2.Bindings[i]))
											return false;

									break;
							}

							return true;
						};

						for (var i = 0; i < e1.Bindings.Count; i++)
						{
							var b1 = e1.Bindings[i];
							var b2 = e2.Bindings[i];

							if (!compareBindings(b1, b2))
								return false;
						}

						return true;
					}

				case ExpressionType.New:
					{
						var e1 = (NewExpression)expr1;
						var e2 = (NewExpression)expr2;

						if (e1.Arguments.Count != e2.Arguments.Count || e1.Members.  Count != e2.Members.  Count || e1.Constructor != e2.Constructor)
							return false;

						for (var i = 0; i < e1.Members.Count; i++)
							if (e1.Members[i] != e2.Members[i])
								return false;

						for (var i = 0; i < e1.Arguments.Count; i++)
							if (!Compare(e1.Arguments[i], e2.Arguments[i]))
								return false;

						return true;
					}

				case ExpressionType.NewArrayBounds:
				case ExpressionType.NewArrayInit:
					{
						var e1 = (NewArrayExpression)expr1;
						var e2 = (NewArrayExpression)expr2;

						if (e1.Expressions.Count != e2.Expressions.Count)
							return false;

						for (var i = 0; i < e1.Expressions.Count; i++)
							if (!Compare(e1.Expressions[i], e2.Expressions[i]))
								return false;

						return true;
					}

				case ExpressionType.Parameter:
					{
						var e1 = (ParameterExpression)expr1;
						var e2 = (ParameterExpression)expr2;
						return e1.Name == e2.Name;
					}

				case ExpressionType.TypeIs:
					{
						var e1 = (TypeBinaryExpression)expr1;
						var e2 = (TypeBinaryExpression)expr2;
						return e1.TypeOperand == e2.TypeOperand && Compare(e1.Expression, e2.Expression);
					}
			}

			throw new InvalidOperationException();
		}

		static bool IsConstant(Type type)
		{
			return type == typeof(int) || type == typeof(string);
		}

		#endregion
	}
}