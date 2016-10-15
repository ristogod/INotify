using System;
using System.Linq.Expressions;

namespace INotify.Core.Extensions
{
    public static class ExpressionExtensions
    {
        #region methods

        public static string GetName<T>(this Expression<Func<T>> property) => (property.Body as MemberExpression)?.Member.Name;
        public static string GetName<TRef, TProp>(this Expression<Func<TRef, TProp>> property) => (property.Body as MemberExpression)?.Member.Name;

        #endregion
    }
}
