Утилита `ReadWriteSynchronizer` проверяет, что, если вызывается сеттер свойства, то вызывается и гетер. И наоборот.  
```csharp
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
```
Утилита `ReadWriteSynchronizer` выдает:
```
В Read методе отсутствуют вызовы следующих свойств:
A002 = e.A002,
```