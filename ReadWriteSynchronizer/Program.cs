using System;
using System.Linq.Expressions;

namespace ReadWriteSynchronizer
{
    class Program
    {
        static void Main()
        {
            ReadWriteSynchronizer.CheckMatch(
                writeMethod: typeof(Program).GetMethod(nameof(FillXEntity)),
                readMethod: ToXDataExpr());
        }

        public void FillXEntity(XEntity entity, XData data)
        {
            entity.A001 = data.A001;
            entity.A002 = data.A002;
        }

        public static Expression<Func<XEntity, XData>> ToXDataExpr()
        {
            return e => new XData {
                A001 = e.A001,
                //A002 = e.A002,
            };
        }
    }

    public class XEntity
    {
        public string A001 { get; set; }
        public string A002 { get; set; }
    }

    public class XData
    {
        public string A001 { get; set; }
        public string A002 { get; set; }
    }
}
