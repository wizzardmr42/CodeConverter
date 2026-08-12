using System.Threading.Tasks;
using ICSharpCode.CodeConverter.Tests.TestRunners;
using Xunit;

namespace ICSharpCode.CodeConverter.Tests.CSharp.ExpressionTests;

public class LinqExpressionTests : ConverterTestBase
{
    [Fact]
    public async Task Issue895_LinqWhereAfterGroupAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"
Imports System.Collections.Generic
Imports System.Linq

Public Class Issue895
    Private Shared Sub LinqWithGroup()
        Dim numbers = New List(Of Integer) From {1, 2, 3, 4, 4}
        Dim duplicates = From x In numbers
                         Group By x Into Group
                         Where Group.Count > 1
        System.Console.WriteLine(duplicates.Count)
    End Sub
End Class",
            @"using System;
using System.Collections.Generic;
using System.Linq;

public partial class Issue895
{
    private static void LinqWithGroup()
    {
        var numbers = new List<int>() { 1, 2, 3, 4, 4 };
        var duplicates = from x in numbers
                         group x by x into Group
                         let x = Group.Key
                         where Group.Count() > 1
                         select new { x, Group };
        Console.WriteLine(duplicates.Count());
    }
}");
    }

    [Fact]
    public async Task Characterize_Issue948_GroupByMember_Async()
    {
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Collections.Generic
Imports System.Linq

Class C
    Public Property MyString As String
End Class

Public Module Module1
    Public Sub Main()
        Dim list As New List(Of C)()
        Dim result = From f In list
                     Group f By f.MyString Into Group
                     Order By MyString
	End Sub
End Module",
            @"using System.Collections.Generic;
using System.Linq;

internal partial class C
{
    public string MyString { get; set; }
}

public static partial class Module1
{
    public static void Main()
    {
        var list = new List<C>();
        var result = from f in list
                     group f by f.MyString into @group
                     let MyString = @group.Key
                     orderby MyString
                     select new { MyString, Group = @group };
    }
}");
    }

    [Fact]
    public async Task Issue736_LinqEarlySelectAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"
Imports System.Collections.Generic
Imports System.Linq

Public Class Issue635
    Dim foo As Object
    Dim l As List(Of Issue635)
    Dim listSelectWhere = From t in l
            Select t.foo
            Where 1 = 2
End Class",
            @"
using System.Collections.Generic;
using System.Linq;

public partial class Issue635
{
    private object foo;
    private List<Issue635> l;
    private object listSelectWhere;

    public Issue635()
    {
        listSelectWhere = from foo in
                              from t in l
                              select t.foo
                          where 1 == 2
                          select foo;
    }
}");
    }

    [Fact]
    public async Task Issue635_LinqDistinctOrderByAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"
Imports System.Collections.Generic
Imports System.Linq

Public Class Issue635
    Dim l As List(Of Integer)
    Dim listSortedDistinct = From x In l Order By x Distinct
End Class",
            @"
using System.Collections.Generic;
using System.Linq;

public partial class Issue635
{
    private List<int> l;
    private object listSortedDistinct;

    public Issue635()
    {
        listSortedDistinct = (from x in l
                              orderby x
                              select x).Distinct();
    }
}");
    }

    [Fact]
    public async Task Linq1Async()
    {
        await TestConversionVisualBasicToCSharpAsync(@"Private Shared Sub SimpleQuery()
    Dim numbers = {7, 9, 5, 3, 6}
    Dim res = From n In numbers Where n > 5 Select n
    For Each n In res
        Console.WriteLine(n)
    Next
End Sub",
            @"private static void SimpleQuery()
{
    int[] numbers = new[] { 7, 9, 5, 3, 6 };
    var res = from n in numbers
              where n > 5
              select n;
    foreach (var n in res)
        Console.WriteLine(n);
}");
    }

    [Fact]
    public async Task Linq2Async()
    {
        await TestConversionVisualBasicToCSharpAsync(@"Public Shared Sub Linq40()
    Dim numbers As Integer() = {5, 4, 1, 3, 9, 8, 6, 7, 2, 0}
    Dim numberGroups = From n In numbers Group n By __groupByKey1__ = n Mod 5 Into g = Group Select New With {Key .Remainder = __groupByKey1__, Key .Numbers = g}
    
    For Each g In numberGroups
        Console.WriteLine($""Numbers with a remainder of { g.Remainder} when divided by 5:"")

        For Each n In g.Numbers
            Console.WriteLine(n)
        Next
    Next
End Sub",
            @"public static void Linq40()
{
    int[] numbers = new[] { 5, 4, 1, 3, 9, 8, 6, 7, 2, 0 };
    var numberGroups = from n in numbers
                       group n by (n % 5) into g
                       let __groupByKey1__ = g.Key
                       select new { Remainder = __groupByKey1__, Numbers = g };

    foreach (var g in numberGroups)
    {
        Console.WriteLine($""Numbers with a remainder of {g.Remainder} when divided by 5:"");

        foreach (var n in g.Numbers)
            Console.WriteLine(n);
    }
}");
    }

    [Fact]
    public async Task Linq3Async()
    {
        await TestConversionVisualBasicToCSharpAsync(@"Class Product
    Public Category As String
    Public ProductName As String
End Class

Class Test

    Public Function GetProductList As Product()
        Return Nothing
    End Function

    Public Sub Linq102()
        Dim categories As String() = New String() {""Beverages"", ""Condiments"", ""Vegetables"", ""Dairy Products"", ""Seafood""}
        Dim products As Product() = GetProductList()
        Dim q = From c In categories Join p In products On c Equals p.Category Select New With {Key .Category = c, p.ProductName}

        For Each v In q
            Console.WriteLine($""{v.ProductName}: {v.Category}"")
        Next
    End Sub
End Class",
            @"using System;
using System.Linq;

internal partial class Product
{
    public string Category;
    public string ProductName;
}

internal partial class Test
{

    public Product[] GetProductList()
    {
        return null;
    }

    public void Linq102()
    {
        string[] categories = new string[] { ""Beverages"", ""Condiments"", ""Vegetables"", ""Dairy Products"", ""Seafood"" };
        Product[] products = GetProductList();
        var q = from c in categories
                join p in products on c equals p.Category
                select new { Category = c, p.ProductName };

        foreach (var v in q)
            Console.WriteLine($""{v.ProductName}: {v.Category}"");
    }
}");
    }

    [Fact]
    public async Task Linq4Async()
    {
        await TestConversionVisualBasicToCSharpAsync(@"Class Product
    Public Category As String
    Public ProductName As String
End Class

Class Test
    Public Function GetProductList As Product()
        Return Nothing
    End Function

    Public Sub Linq103()
        Dim categories As String() = New String() {""Beverages"", ""Condiments"", ""Vegetables"", ""Dairy Products"", ""Seafood""}
        Dim products = GetProductList()
        Dim q = From c In categories Group Join p In products On c Equals p.Category Into ps = Group Select New With {Key .Category = c, Key .Products = ps}

        For Each v In q
            Console.WriteLine(v.Category & "":"")

            For Each p In v.Products
                Console.WriteLine(""   "" & p.ProductName)
            Next
        Next
    End Sub
End Class", @"using System;
using System.Linq;

internal partial class Product
{
    public string Category;
    public string ProductName;
}

internal partial class Test
{
    public Product[] GetProductList()
    {
        return null;
    }

    public void Linq103()
    {
        string[] categories = new string[] { ""Beverages"", ""Condiments"", ""Vegetables"", ""Dairy Products"", ""Seafood"" };
        Product[] products = GetProductList();
        var q = from c in categories
                join p in products on c equals p.Category into ps
                select new { Category = c, Products = ps };

        foreach (var v in q)
        {
            Console.WriteLine(v.Category + "":"");

            foreach (var p in v.Products)
                Console.WriteLine(""   "" + p.ProductName);
        }
    }
}");
    }

    [Fact]
    public async Task Linq5Async()
    {
        await TestConversionVisualBasicToCSharpAsync(@"Private Shared Function FindPicFilePath(AList As List(Of FileInfo), picId As String) As String
    For Each FileInfo As FileInfo In From FileInfo1 In AList Where FileInfo1.Name.Substring(0, 6) = picId
        Return FileInfo.FullName
    Next
    Return String.Empty
End Function", @"private static string FindPicFilePath(List<FileInfo> AList, string picId)
{
    foreach (FileInfo FileInfo in from FileInfo1 in AList
                                  where FileInfo1.Name.Substring(0, 6) == picId
                                  select FileInfo1)
        return FileInfo.FullName;
    return string.Empty;
}");
    }

    [Fact]
    public async Task LinqAsEnumerableAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Data

Public Class AsEnumerableTest
    Public Sub FillImgColor()
        Dim dtsMain As New DataSet
        For Each i_ColCode As Integer In 
            From CurRow In dtsMain.Tables(""tb_Color"") Select CInt(CurRow.Item(""i_ColCode""))
        Next
    End Sub
End Class", @"using System.Data;
using System.Linq;
using Microsoft.VisualBasic.CompilerServices; // Install-Package Microsoft.VisualBasic

public partial class AsEnumerableTest
{
    public void FillImgColor()
    {
        var dtsMain = new DataSet();
        foreach (int i_ColCode in from CurRow in dtsMain.Tables[""tb_Color""].AsEnumerable()
                                  select Conversions.ToInteger(CurRow[""i_ColCode""]))
        {
        }
    }
}");
    }

    [Fact]
    public async Task LinqMultipleFromsAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"Private Shared Sub LinqSub()
    Dim _result = From _claimProgramSummary In New List(Of List(Of List(Of List(Of String))))()
                  From _claimComponentSummary In _claimProgramSummary.First()
                  From _lineItemCalculation In _claimComponentSummary.Last()
                  Select _lineItemCalculation
End Sub", @"private static void LinqSub()
{
    var _result = from _claimProgramSummary in new List<List<List<List<string>>>>()
                  from _claimComponentSummary in _claimProgramSummary.First()
                  from _lineItemCalculation in _claimComponentSummary.Last()
                  select _lineItemCalculation;
}");
    }

    [Fact]
    public async Task LinqNoFromsAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"Public Class VisualBasicClass
    Public Shared Sub X(objs As List(Of Object))
        Dim MaxObj As Integer = Aggregate o In objs Into Max(o.GetHashCode())
        Dim CountWhereObj As Integer = Aggregate o In objs Where o.GetHashCode() > 3 Into Count()
    End Sub
End Class", @"using System.Collections.Generic;
using System.Linq;

public partial class VisualBasicClass
{
    public static void X(List<object> objs)
    {
        int MaxObj = objs.Max(o => o.GetHashCode());
        int CountWhereObj = (from o in objs
                             where o.GetHashCode() > 3
                             select o).Count();
    }
}");
    }

    [Fact]
    public async Task LinqPartitionDistinctAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"Private Shared Function FindPicFilePath() As IEnumerable(Of String)
    Dim words = {""an"", ""apple"", ""a"", ""day"", ""keeps"", ""the"", ""doctor"", ""away""}

    Return From word In words
            Skip 1
            Skip While word.Length >= 1
            Take While word.Length < 5
            Take 2
            Distinct
End Function", @"private static IEnumerable<string> FindPicFilePath()
{
    string[] words = new[] { ""an"", ""apple"", ""a"", ""day"", ""keeps"", ""the"", ""doctor"", ""away"" };

    return words.Skip(1).SkipWhile(word => word.Length >= 1).TakeWhile(word => word.Length < 5).Take(2).Distinct();
}");
    }

    [Fact(Skip = "Issue #29 - Aggregate not supported")]
    public async Task LinqAggregateSumAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"Private Shared Sub ASub()
    Dim expenses() As Double = {560.0, 300.0, 1080.5, 29.95, 64.75, 200.0}
    Dim totalExpense = Aggregate expense In expenses Into Sum()
End Sub", @"private static void ASub()
{
    double[] expenses = {560.0, 300.0, 1080.5, 29.95, 64.75, 200.0};
    var totalExpense = expenses.Sum();
}");
    }

    [Fact(Skip = "Issue #29 - Group join not supported")]
    public async Task LinqGroupJoinAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"Private Shared Sub ASub()
    Dim customerList = From cust In customers
                       Group Join ord In orders On
                       cust.CustomerID Equals ord.CustomerID
                       Into CustomerOrders = Group,
                            OrderTotal = Sum(ord.Total)
                       Select cust.CompanyName, cust.CustomerID,
                              CustomerOrders, OrderTotal
End Sub", @"private static void ASub()
{
    var customerList = from cust in customers
                       join ord in orders on cust.CustomerID equals ord.CustomerID into CustomerOrders
                       let OrderTotal = Sum(ord.Total) //TODO Figure out exact C# syntax for this query
                       select new { cust.CompanyName, cust.CustomerID, CustomerOrders, OrderTotal };
}");
    }

    [Fact]
    public async Task LinqJoinReorderExpressionsAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"Class Customer
    Public CustomerID As String
    Public CompanyName As String
End Class

Class Order
    Public CustomerID As String
    Public Total As String
End Class

Class Test
Private Shared Sub ASub()
    Dim customers = New List(Of Customer)
    Dim orders = New List(Of Order)
    Dim customerList = From cust In customers
                       Join ord In orders On ord.CustomerID Equals cust.CustomerID
                       Select cust.CompanyName, ord.Total
End Sub
End Class", @"using System.Collections.Generic;
using System.Linq;

internal partial class Customer
{
    public string CustomerID;
    public string CompanyName;
}

internal partial class Order
{
    public string CustomerID;
    public string Total;
}

internal partial class Test
{
    private static void ASub()
    {
        var customers = new List<Customer>();
        var orders = new List<Order>();
        var customerList = from cust in customers
                           join ord in orders on cust.CustomerID equals ord.CustomerID
                           select new { cust.CompanyName, ord.Total };
    }
}");
    }

    [Fact]
    public async Task LinqMultipleJoinConditionsReorderExpressionsAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"Class Customer
    Public CustomerID As String
    Public CompanyName As String
End Class

Class Order
    Public CustomerID As String
    Public Total As String
End Class

Class Test
Private Shared Sub ASub()
    Dim customers = New List(Of Customer)
    Dim orders = New List(Of Order)
    Dim customerList = From cust In customers
                       Join ord In orders On ord.CustomerID Equals cust.CustomerID And cust.CompanyName Equals ord.Total
                       Select cust.CompanyName, ord.Total
End Sub
End Class", @"using System.Collections.Generic;
using System.Linq;

internal partial class Customer
{
    public string CustomerID;
    public string CompanyName;
}

internal partial class Order
{
    public string CustomerID;
    public string Total;
}

internal partial class Test
{
    private static void ASub()
    {
        var customers = new List<Customer>();
        var orders = new List<Order>();
        var customerList = from cust in customers
                           join ord in orders on new { key0 = cust.CustomerID, key1 = cust.CompanyName } equals new { key0 = ord.CustomerID, key1 = ord.Total }
                           select new { cust.CompanyName, ord.Total };
    }
}");
    }

    [Fact]
    public async Task LinqMultipleIdentifierOnlyJoinConditionsReorderExpressionsAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"Class Customer
    Public CustomerID As String
    Public CompanyName As String
End Class

Class Order
    Public Customer As Customer
    Public Total As String
End Class

Class Test
Private Shared Sub ASub()
    Dim customers = New List(Of Customer)
    Dim orders = New List(Of Order)
    Dim customerList = From cust In customers
                       Join ord In orders On ord.Customer Equals cust And cust.CompanyName Equals ord.Total
                       Select cust.CompanyName, ord.Total
End Sub
End Class", @"using System.Collections.Generic;
using System.Linq;

internal partial class Customer
{
    public string CustomerID;
    public string CompanyName;
}

internal partial class Order
{
    public Customer Customer;
    public string Total;
}

internal partial class Test
{
    private static void ASub()
    {
        var customers = new List<Customer>();
        var orders = new List<Order>();
        var customerList = from cust in customers
                           join ord in orders on new { key0 = cust, key1 = cust.CompanyName } equals new { key0 = ord.Customer, key1 = ord.Total }
                           select new { cust.CompanyName, ord.Total };
    }
}");
    }

    [Fact]
    public async Task LinqGroupByTwoThingsAnonymouslyAsync()
    {
        // VB `Group By <k1>, <k2> Into Group` produces an anonymous shape
        // `{ k1, k2, Group }` where downstream `.k1` / `.Group` both work
        // (a `System.Linq.IGrouping<K,T>` doesn't). C# needs an explicit
        // `into @group select new { @group.Key.k1, @group.Key.k2, Group = @group }`
        // continuation to preserve the shape. Bug 2a / #1080-adjacent.
        await TestConversionVisualBasicToCSharpAsync(@"Public Class Class1
    Sub Foo()
        Dim xs As New List(Of String)
        Dim y = From x In xs Group By x.Length, x.Count() Into Group
    End Sub
End Class", @"using System.Collections.Generic;
using System.Linq;

public partial class Class1
{
    public void Foo()
    {
        var xs = new List<string>();
        var y = from x in xs
                group x by new { x.Length, Count = x.Count() } into Group
                select new { Group.Key.Length, Group.Key.Count, Group };
    }
}");
    }

    [Fact]
    public async Task LinqSelectVariableDeclarationAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"Imports System
Imports System.Linq

Public Class Class717
        Sub Main()
        Dim arr(1) as Integer
        arr(0) = 0
        arr(1) = 1

        Dim r = From e In arr
                Select p = $""value: {e}""
                Select l = p.Substring(1)
                Select x = l

        For each m In r
            Console.WriteLine(m)
        Next
    End Sub
End Class", @"using System;
using System.Linq;

public partial class Class717
{
    public void Main()
    {
        var arr = new int[2];
        arr[0] = 0;
        arr[1] = 1;

        var r = from e in arr
                let p = $""value: {e}""
                let l = p.Substring(1)
                select l;

        foreach (var m in r)
            Console.WriteLine(m);
    }
}");
    }

    [Fact]
    public async Task LinqGroupByAnonymousAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Runtime.CompilerServices ' Removed by simplifier

Public Class AccountEntry
    Public Property LookupAccountEntryTypeId As Object
    Public Property LookupAccountEntrySourceId As Object
    Public Property SponsorId As Object
    Public Property LookupFundTypeId As Object
    Public Property StartDate As Object
    Public Property SatisfiedDate As Object
    Public Property InterestStartDate As Object
    Public Property ComputeInterestFlag As Object
    Public Property SponsorClaimRevision As Object
    Public Property Amount As Decimal
    Public Property AccountTransactions As List(Of Object)
    Public Property AccountEntryClaimDetails As List(Of AccountEntry)
End Class

Module Ext
    <Extension>
    Public Function Reduce(ByVal accountEntries As IEnumerable(Of AccountEntry)) As IEnumerable(Of AccountEntry)
        Return (
            From _accountEntry In accountEntries
                Where _accountEntry.Amount > 0D
                Group By _keys = New With
                    {
                    Key .LookupAccountEntryTypeId = _accountEntry.LookupAccountEntryTypeId,
                    Key .LookupAccountEntrySourceId = _accountEntry.LookupAccountEntrySourceId,
                    Key .SponsorId = _accountEntry.SponsorId,
                    Key .LookupFundTypeId = _accountEntry.LookupFundTypeId,
                    Key .StartDate = _accountEntry.StartDate,
                    Key .SatisfiedDate = _accountEntry.SatisfiedDate,
                    Key .InterestStartDate = _accountEntry.InterestStartDate,
                    Key .ComputeInterestFlag = _accountEntry.ComputeInterestFlag,
                    Key .SponsorClaimRevision = _accountEntry.SponsorClaimRevision
                    } Into Group
                Select New AccountEntry() With
                    {
                    .LookupAccountEntryTypeId = _keys.LookupAccountEntryTypeId,
                    .LookupAccountEntrySourceId = _keys.LookupAccountEntrySourceId,
                    .SponsorId = _keys.SponsorId,
                    .LookupFundTypeId = _keys.LookupFundTypeId,
                    .StartDate = _keys.StartDate,
                    .SatisfiedDate = _keys.SatisfiedDate,
                    .ComputeInterestFlag = _keys.ComputeInterestFlag,
                    .InterestStartDate = _keys.InterestStartDate,
                    .SponsorClaimRevision = _keys.SponsorClaimRevision,
                    .Amount = Group.Sum(Function(accountEntry) accountEntry.Amount),
                    .AccountTransactions = New List(Of Object)(),
                    .AccountEntryClaimDetails =
                        (From _accountEntry In Group From _claimDetail In _accountEntry.AccountEntryClaimDetails
                            Select _claimDetail).Reduce().ToList
                    }
            )
    End Function
End Module", @"using System.Collections.Generic;
using System.Linq;

public partial class AccountEntry
{
    public object LookupAccountEntryTypeId { get; set; }
    public object LookupAccountEntrySourceId { get; set; }
    public object SponsorId { get; set; }
    public object LookupFundTypeId { get; set; }
    public object StartDate { get; set; }
    public object SatisfiedDate { get; set; }
    public object InterestStartDate { get; set; }
    public object ComputeInterestFlag { get; set; }
    public object SponsorClaimRevision { get; set; }
    public decimal Amount { get; set; }
    public List<object> AccountTransactions { get; set; }
    public List<AccountEntry> AccountEntryClaimDetails { get; set; }
}

internal static partial class Ext
{
    public static IEnumerable<AccountEntry> Reduce(this IEnumerable<AccountEntry> accountEntries)
    {
        return from _accountEntry in accountEntries
               where _accountEntry.Amount > 0m
               group _accountEntry by new
               {
                   _accountEntry.LookupAccountEntryTypeId,
                   _accountEntry.LookupAccountEntrySourceId,
                   _accountEntry.SponsorId,
                   _accountEntry.LookupFundTypeId,
                   _accountEntry.StartDate,
                   _accountEntry.SatisfiedDate,
                   _accountEntry.InterestStartDate,
                   _accountEntry.ComputeInterestFlag,
                   _accountEntry.SponsorClaimRevision
               } into Group
               let _keys = Group.Key
               select new AccountEntry()
               {
                   LookupAccountEntryTypeId = _keys.LookupAccountEntryTypeId,
                   LookupAccountEntrySourceId = _keys.LookupAccountEntrySourceId,
                   SponsorId = _keys.SponsorId,
                   LookupFundTypeId = _keys.LookupFundTypeId,
                   StartDate = _keys.StartDate,
                   SatisfiedDate = _keys.SatisfiedDate,
                   ComputeInterestFlag = _keys.ComputeInterestFlag,
                   InterestStartDate = _keys.InterestStartDate,
                   SponsorClaimRevision = _keys.SponsorClaimRevision,
                   Amount = Group.Sum(accountEntry => accountEntry.Amount),
                   AccountTransactions = new List<object>(),
                   AccountEntryClaimDetails = (from _accountEntry in Group
                                               from _claimDetail in _accountEntry.AccountEntryClaimDetails
                                               select _claimDetail).Reduce().ToList()
               };
    }
}");
    }


    [Fact]
    public async Task LinqCommasToFromAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Collections.Generic
Imports System.Linq

Public Class VisualBasicClass
    Sub Main
	    Dim list1 As New List(Of Integer)() From {1,2,3}
	    Dim list2 As New List(Of Integer) From {2, 4,5}
	
	    Dim qs = From n In list1, x In list2
			     Where x = n 
			     Select New With {x, n}
    End Sub
End Class
",
            @"using System.Collections.Generic;
using System.Linq;

public partial class VisualBasicClass
{
    public void Main()
    {
        var list1 = new List<int>() { 1, 2, 3 };
        var list2 = new List<int>() { 2, 4, 5 };

        var qs = from n in list1
                 from x in list2
                 where x == n
                 select new { x, n };
    }
}");
    }


    [Fact]
    public async Task Issue1011_LinqExpressionWithNullableCharacterizationAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"Public Class ConversionTest2
    Private Class MyEntity
        Property FavoriteNumber As Integer?
        Property Name As String
    End Class
    Private Sub BugRepro()

        Dim entities As New List(Of MyEntity)

        Dim result As String = (From e In entities
                                Where e.FavoriteNumber = 123
                                Select e.Name).Single

    End Sub
End Class
",
            @"using System.Collections.Generic;
using System.Linq;

public partial class ConversionTest2
{
    private partial class MyEntity
    {
        public int? FavoriteNumber { get; set; }
        public string Name { get; set; }
    }
    private void BugRepro()
    {

        var entities = new List<MyEntity>();

        string result = (from e in entities
                         where e.FavoriteNumber == 123
                         select e.Name).Single();

    }
}");
    }


    [Fact]
    public async Task AnExpressionTreeMayNotContainIsAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"Public Class ConversionTest6
    Private Class MyEntity
        Property Name As String
        Property FavoriteString As String
    End Class
    Public Sub BugRepro()

        Dim entities = New List(Of MyEntity) ' If this was a DbSet from EFCore, then the 'is' below needs to be converted to == to avoid an error. Instead of detecting dbset, we'll just do this for all queries

        Dim data = (From e In entities
                    Where e.Name Is Nothing OrElse e.FavoriteString IsNot Nothing
                    Select e).ToList
    End Sub
End Class
",
            @"using System.Collections.Generic;
using System.Linq;

public partial class ConversionTest6
{
    private partial class MyEntity
    {
        public string Name { get; set; }
        public string FavoriteString { get; set; }
    }
    public void BugRepro()
    {

        var entities = new List<MyEntity>(); // If this was a DbSet from EFCore, then the 'is' below needs to be converted to == to avoid an error. Instead of detecting dbset, we'll just do this for all queries

        var data = (from e in entities
                    where e.Name == null || e.FavoriteString != null
                    select e).ToList();
    }
}");
    }

    [Fact]
    public async Task GroupJoinProjectsFromVarAndIntoVarAsync()
    {
        // VB `Group Join <j> In ... Into <n>` implicitly projects `{<from>,<into>}`
        // — downstream `result.<from>` and `result.<into>` both work. Codeconv
        // used to emit `select <from>` and drop the into variable.
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Collections.Generic
Imports System.Linq

Public Class SC
    Public Property ID As Integer
End Class

Public Class SL
    Public Property SCID As Integer
    Public Property Note As String
End Class

Public Module M
    Public Sub Do1()
        Dim scs As New List(Of SC)
        Dim sls As New List(Of SL)
        Dim r = From sc In scs
                Group Join sl In sls On sc.ID Equals sl.SCID Into details = Group
                Where details.Any()
                Select sc.ID, DetailCount = details.Count
    End Sub
End Module",
            @"using System.Collections.Generic;
using System.Linq;

public partial class SC
{
    public int ID { get; set; }
}

public partial class SL
{
    public int SCID { get; set; }
    public string Note { get; set; }
}

public static partial class M
{
    public static void Do1()
    {
        var scs = new List<SC>();
        var sls = new List<SL>();
        var r = from sc in scs
                join sl in sls on sc.ID equals sl.SCID into details
                where details.Any()
                select new { sc.ID, DetailCount = details.Count() };
    }
}");
    }

    [Fact]
    public async Task AnonymousTypeReassignmentSkipsInvalidVarCastAsync()
    {
        // VB `q2 = From x In q2 ...` where q2 was declared as an anonymous-typed
        // IEnumerable: codeconv used to emit `(var)(from x in q2 ...)` which is
        // a parse error (CS0825). Drop the cast — assignment is type-safe by
        // inference.
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Collections.Generic
Imports System.Linq

Public Module M
    Public Sub Do1()
        Dim src As New List(Of Integer)
        Dim q = From x In src Group By x Into Value = Sum(x)
        q = From x In q Order By x.Value
    End Sub
End Module",
            @"using System.Collections.Generic;
using System.Linq;

public static partial class M
{
    public static void Do1()
    {
        var src = new List<int>();
        var q = from x in src
                group x by x into Group
                let x = Group.Key
                select new { x = Group.Key, Value = Group.Sum(x => x) };
        q = from x in q
            orderby x.Value
            select x;
    }
}");
    }

    [Fact]
    public async Task GroupByAggregationArgumentBecomesLambdaAsync()
    {
        // VB `Group By ... Into Total = Sum(x.V)` — the aggregation argument
        // `x.V` is evaluated per group element. In C# this becomes
        // `Group.Sum(x => x.V)`. Codeconv used to emit `Group.Sum()` (no arg)
        // which triggers CS1929 on IGrouping<K, T> where T isn't numeric.
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Collections.Generic
Imports System.Linq

Public Class Row
    Public Property Bucket As Integer
    Public Property Value As Integer
End Class

Public Module M
    Public Sub Do1()
        Dim rows As New List(Of Row)
        Dim r = From x In rows
                Group By x.Bucket, x.Value Into Total = Sum(x.Value)
    End Sub
End Module",
            @"using System.Collections.Generic;
using System.Linq;

public partial class Row
{
    public int Bucket { get; set; }
    public int Value { get; set; }
}

public static partial class M
{
    public static void Do1()
    {
        var rows = new List<Row>();
        var r = from x in rows
                group x by new { x.Bucket, x.Value } into Group
                select new { Group.Key.Bucket, Group.Key.Value, Total = Group.Sum(x => x.Value) };
    }
}");
    }

    [Fact]
    public async Task NullableBoolLambdaBodyIsUnwrappedForBoolPredicateAsync()
    {
        // VB `.Any(Function(x) x.NullableDate > cutoff)` — the comparison produces
        // Boolean? because the LHS is nullable. When the lambda's target delegate
        // returns bool, unwrap with `?? false` so it satisfies `Func<T, bool>`.
        // Otherwise CS0266 / CS1662.
        await TestConversionVisualBasicToCSharpAsync(@"Imports System
Imports System.Collections.Generic
Imports System.Linq

Public Class Item
    Public Property Stamp As Date?
End Class

Public Module M
    Public Function Fresh(items As IEnumerable(Of Item), cutoff As Date) As Boolean
        Return items.Any(Function(i) i.Stamp > cutoff)
    End Function
End Module",
            @"using System;
using System.Collections.Generic;
using System.Linq;

public partial class Item
{
    public DateTime? Stamp { get; set; }
}

public static partial class M
{
    public static bool Fresh(IEnumerable<Item> items, DateTime cutoff)
    {
        return items.Any(i => (i.Stamp is { } arg1 ? arg1 > cutoff : (bool?)null) ?? false);
    }
}");
    }

    [Fact]
    public async Task NestedFuncLambdaInsideExpressionTreeSuppressesIsPatternAsync()
    {
        // A Func-typed lambda nested inside an outer Expression<Func<...>>
        // (e.g. `where !s.Kids.Any(k => k.Stamp > cutoff)` in an IQueryable
        // query) is still going to be translated as part of the outer
        // expression tree — patterns aren't allowed there (CS8122). Suppress
        // the `is { }` pattern-match null-safe transform for the inner lambda
        // by inheriting the outer IsWithinQuery flag.
        await TestConversionVisualBasicToCSharpAsync(@"Imports System
Imports System.Collections.Generic
Imports System.Linq

Public Class Kid
    Public Property Stamp As Date?
End Class

Public Class Parent
    Public Property Kids As ICollection(Of Kid)
End Class

Public Module M
    Public Function Filter(src As IQueryable(Of Parent), cutoff As Date) As IEnumerable(Of Parent)
        Return From p In src
               Where Not p.Kids.Any(Function(k) k.Stamp > cutoff)
               Select p
    End Function
End Module",
            @"using System;
using System.Collections.Generic;
using System.Linq;

public partial class Kid
{
    public DateTime? Stamp { get; set; }
}

public partial class Parent
{
    public ICollection<Kid> Kids { get; set; }
}

public static partial class M
{
    public static IEnumerable<Parent> Filter(IQueryable<Parent> src, DateTime cutoff)
    {
        return from p in src
               where !p.Kids.Any(k => k.Stamp > cutoff)
               select p;
    }
}");
    }

    [Fact]
    public async Task SelectWithRetainedRangeVarStaysAccessibleAsync()
    {
        // VB `Select x, Extra = ...` where the first item is a bare reference
        // to the current range variable creates an anonymous type `{x, Extra}`
        // BUT VB's transparent-identifier magic keeps `x.Member` accessible in
        // subsequent clauses (Where/Group By/Order By/Select).
        //
        // C# `select new { x, Extra = ... }` loses that transparency: the range
        // variable becomes the anon type, so `x.Member` fails with CS1061
        // "does not contain a definition for 'Member'". Codeconv currently
        // emits the anon-type projection form.
        //
        // Correct C# emission: keep the range variable and introduce `let`
        // clauses for the extra members:
        //   from x in src
        //   let Extra = f(x)
        //   where g(x.Member, Extra)
        //   select ...
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Collections.Generic
Imports System.Linq

Public Class Row
    Public Property ID As Integer
    Public Property Category As String
End Class

Public Module M
    Public Sub Do1()
        Dim src As New List(Of Row)
        Dim r = From x In src
                Select x, Prefix = x.Category.Substring(0, 1)
                Where x.ID > 0
                Order By Prefix, x.ID
                Select x.ID, Prefix
    End Sub
End Module",
            @"using System.Collections.Generic;
using System.Linq;

public partial class Row
{
    public int ID { get; set; }
    public string Category { get; set; }
}

public static partial class M
{
    public static void Do1()
    {
        var src = new List<Row>();
        var r = from x in src
                let Prefix = x.Category.Substring(0, 1)
                where x.ID > 0
                orderby Prefix, x.ID
                select new { x.ID, Prefix };
    }
}");
    }

    [Fact(Skip = "TDD: Select-rename with Distinct (CS0103 cluster ~17 sites — u/o/k/z single-letter loop vars)")]
    public async Task SelectRenameWithDistinctPreservesRenamedVarAsync()
    {
        // VB `Select u = r.AuthorisedBy Distinct` renames the range variable
        // to `u`, applies Distinct, and subsequent clauses (`Where u.X`,
        // `Order By u.Y`) use `u`. Codeconv emits:
        //   from r in src let u = r.AuthorisedBy select r).Distinct()
        // — dropping `u` at the select (it kept `r`) and losing it in the
        // outer scope. Result: `u.PasswordHash` etc. fail with CS0103.
        //
        // Correct emission: project TO u then Distinct, preserving u:
        //   from u in (from r in src select r.AuthorisedBy).Distinct()
        //   where u.PasswordHash != "" ...
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Collections.Generic
Imports System.Linq

Public Class User
    Public Property PasswordHash As String
    Public Property Surname As String
End Class

Public Class Row
    Public Property HasUserId As Boolean
    Public Property User As User
End Class

Public Module M
    Public Sub Do1()
        Dim src As New List(Of Row)
        Dim r = (From row In src Where row.HasUserId
                 Select u = row.User Distinct
                 Where u.PasswordHash <> """"
                 Order By u.Surname
                 Select u.Surname).ToList()
    End Sub
End Module",
            @"using System.Collections.Generic;
using System.Linq;

public partial class User
{
    public string PasswordHash { get; set; }
    public string Surname { get; set; }
}

public partial class Row
{
    public bool HasUserId { get; set; }
    public User User { get; set; }
}

public static partial class M
{
    public static void Do1()
    {
        var src = new List<Row>();
        var r = (from u in (from row in src
                            where row.HasUserId
                            select row.User).Distinct()
                 where u.PasswordHash != """"
                 orderby u.Surname
                 select u.Surname).ToList();
    }
}");
    }

    [Fact]
    public async Task SelectWithRetainedRangeVarWorksWhenBareIdentifierIsNotFirstAsync()
    {
        // Same transparency preservation as SelectWithRetainedRangeVar...
        // but the bare range-var reference is the SECOND item (VB order
        // doesn't dictate which item is the range var). The chained-Select
        // pattern in BMCore's GetUnitDataFromPO looks like this.
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Collections.Generic
Imports System.Linq

Public Class Inner
    Public Property Cat As String
End Class

Public Class Row
    Public Property Inner As Inner
    Public Property ID As Integer
End Class

Public Module M
    Public Sub Do1()
        Dim src As New List(Of Row)
        Dim r = From x In src
                Select x.Inner, x
                Where x.ID > 0
                Order By Inner.Cat, x.ID
                Select x.ID, Inner
    End Sub
End Module",
            @"using System.Collections.Generic;
using System.Linq;

public partial class Inner
{
    public string Cat { get; set; }
}

public partial class Row
{
    public Inner Inner { get; set; }
    public int ID { get; set; }
}

public static partial class M
{
    public static void Do1()
    {
        var src = new List<Row>();
        var r = from x in src
                let Inner = x.Inner
                where x.ID > 0
                orderby Inner.Cat, x.ID
                select new { x.ID, Inner };
    }
}");
    }

    [Fact]
    public async Task IfBinaryOnNullableEnumWithIntDefaultAsync()
    {
        // VB `If(item.Recorded, 999)` where Recorded is `MyEnum?` — VB
        // allows implicit conversion of 999 to the enum type. Codeconv
        // emits `item.Recorded ?? 999` — CS0019 because `??` needs
        // matching types and enum? / int aren't compatible.
        //
        // Correct: cast the default to the enum, or the LHS to underlying int:
        //   item.Recorded ?? (MyEnum)999
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Collections.Generic
Imports System.Linq

Public Enum MyEnum
    A
    B
End Enum

Public Class Item
    Public Property Recorded As MyEnum?
End Class

Public Module M
    Public Sub Do1()
        Dim items As New List(Of Item)
        Dim r = items.OrderBy(Function(x) If(x.Recorded, 999)).ToList()
    End Sub
End Module",
            @"using System.Collections.Generic;
using System.Linq;

public enum MyEnum
{
    A,
    B
}

public partial class Item
{
    public MyEnum? Recorded { get; set; }
}

public static partial class M
{
    public static void Do1()
    {
        var items = new List<Item>();
        var r = items.OrderBy(x => x.Recorded ?? (MyEnum)999).ToList();
    }
}");
    }

    [Fact]
    public async Task IfBinaryOnNullableShortWithStringDefaultAsync()
    {
        // VB `If(l.CourierID, "")` where CourierID is `short?` and default
        // is string — VB implicitly ToString()s the LHS. Codeconv emits
        // `(l.CourierID) ?? ("")` — CS0019 because short? and string aren't
        // compatible.
        //
        // Correct: `l.CourierID?.ToString() ?? ""` — treats null as "".
        await TestConversionVisualBasicToCSharpAsync(@"Public Class Line
    Public Property CourierID As Short?
End Class

Public Module M
    Public Function Label(l As Line) As String
        Return If(l.CourierID, """")
    End Function
End Module",
            @"using Microsoft.VisualBasic.CompilerServices; // Install-Package Microsoft.VisualBasic

public partial class Line
{
    public short? CourierID { get; set; }
}

public static partial class M
{
    public static string Label(Line l)
    {
        return Conversions.ToString(l.CourierID?.ToString() ?? """");
    }
}");
    }

    [Fact]
    public async Task DecimalPlusDoublePromotesToDecimalAsync()
    {
        // VB permits `decimalVal + doubleVal` — implicitly promotes one to
        // the other. Codeconv's TypeConversionAnalyzer already handles the
        // simple case: emit `(decimal)((double)a + b)` (promote to double,
        // widen result back). Regression test only — BMCore's remaining
        // CS0019 `decimal + double` sites (ProvisionReportModel etc.) hit a
        // more complex variant where the conversion doesn't trigger; those
        // still need investigation.
        await TestConversionVisualBasicToCSharpAsync(@"Public Module M
    Public Function Add(a As Decimal, b As Double) As Decimal
        Return a + b
    End Function
End Module",
            @"
public static partial class M
{
    public static decimal Add(decimal a, double b)
    {
        return (decimal)((double)a + b);
    }
}");
    }

    [Fact(Skip = "TDD: VB `+=` on custom type where only widening `+` operator is defined (CS0019)")]
    public async Task CompoundAssignOnCustomTypeAsync()
    {
        // VB permits `ret += x` when `ret` and `x` are the same custom
        // type and a widening `+` operator returning a compatible type is
        // defined. Codeconv emits `ret += x` which C# rejects with CS0019
        // because C# doesn't auto-synthesise `+=` from `+`.
        //
        // Correct: unfold to `ret = ret + x`.
        await TestConversionVisualBasicToCSharpAsync(@"Public Class Sql
    Public Shared Widening Operator CType(s As String) As Sql
        Return New Sql()
    End Operator
    Public Shared Operator +(a As Sql, b As Sql) As Sql
        Return New Sql()
    End Operator
End Class

Public Module M
    Public Sub Do1()
        Dim ret As Sql = """"
        ret += CType("" more"", Sql)
    End Sub
End Module",
            @"public partial class Sql
{
    public static implicit operator Sql(string s)
    {
        return new Sql();
    }
    public static Sql operator +(Sql a, Sql b)
    {
        return new Sql();
    }
}

public static partial class M
{
    public static void Do1()
    {
        Sql ret = """";
        ret = ret + (Sql)"" more"";
    }
}");
    }

    [Fact(Skip = "TDD: DataRowCollection needs .Cast<DataRow>() (CS1934, ~8 sites) — semantic-model precondition needs revisiting; test scaffold doesn't fully bind")]
    public async Task DataRowCollectionQuerySourceGetsCastAsync()
    {
        // VB `From dr In dataTable.Rows` — VB implicitly enumerates the
        // untyped DataRowCollection as DataRow. C# LINQ requires a typed
        // source, so `from dr in dt.Rows` fails: `DataRowCollection` has no
        // `Select` and codeconv can't infer `dr`'s type — CS1934 "could not
        // find an implementation of the query pattern for source type
        // 'DataRowCollection'".
        //
        // Correct emission: insert `.Cast<DataRow>()` on the source.
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Data
Imports System.Linq

Public Module M
    Public Sub Do1(dt As DataTable)
        Dim r = (From dr In dt.Rows Select dr(""Name"")).ToList()
    End Sub
End Module",
            @"using System.Data;
using System.Linq;

public static partial class M
{
    public static void Do1(DataTable dt)
    {
        var r = (from dr in dt.Rows.Cast<DataRow>()
                 select dr[""Name""]).ToList();
    }
}");
    }

    // -------------------------------------------------------------------
    // TDD markers for remaining known BMCore clusters — each Skip= reason
    // documents the pattern + count + intended fix. Un-skip to work on it.
    // -------------------------------------------------------------------

    [Fact(Skip = "TDD REVISED: Anon-type member named `AsEnumerable` (CS1929 x21) — the isolated test scenario COMPILES cleanly (see body), so the CS1929 in BMCore must have a context-dependent cause. Suspicion: `<Reference Include=\"System.Data.DataSetExtensions\" />` + `using System.Data` in the BMCore project brings DataTableExtensions.AsEnumerable into scope and confuses resolution somehow — but a minimal repro that mirrors that context also passes. Investigate against actual BMCore build (not test scaffold) before fixing")]
    public async Task AnonTypeMemberNamedAsEnumerableResolvesToPropertyAsync()
    {
        // Keep the reproducer for future investigation. The emission itself
        // is `g.AsEnumerable` accessing the anon-type property — that shape
        // works here (only CS1023 unrelated fires from the missing braces).
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Collections.Generic
Imports System.Linq

Public Class Row
    Public Property CountryID As Short
    Public Property Value As Integer
End Class

Public Module M
    Public Sub Do1()
        Dim rows As New List(Of Row)
        For Each item In (From r In rows Group By cid = r.CountryID Into AsEnumerable).ToList
            Dim d = (From x In item.AsEnumerable Select x.Value).ToList
        Next
    End Sub
End Module",
            @"using System.Collections.Generic;
using System.Linq;

public partial class Row
{
    public short CountryID { get; set; }
    public int Value { get; set; }
}

public static partial class M
{
    public static void Do1()
    {
        var rows = new List<Row>();
        foreach (var item in (from r in rows
                              group r by r.CountryID into Group
                              let cid = Group.Key
                              select new { cid = Group.Key, AsEnumerable = Group.AsEnumerable() }).ToList())
            var d = (from x in item.AsEnumerable
                     select x.Value).ToList();
    }
}
1 target compilation errors:
CS1023: Embedded statement cannot be a declaration or labeled statement");
    }

    [Fact]
    public async Task GroupByImplicitSelectProjectsKeyAndAggregationsAsync()
    {
        // BMCore PickListUpdater: `From pp In pps Group By pp.Wave Into
        // AsEnumerable Order By AsEnumerable.Count Descending` (no explicit
        // Select). The VB result element is the anonymous shape
        // `{Wave, AsEnumerable}` — downstream does `w.Wave` / `w.AsEnumerable`.
        // The let-emission path bound those names for in-query clauses but the
        // implicit final select still emitted `select Group`, handing
        // downstream a bare IGrouping with neither member (CS1061 x18).
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Collections.Generic
Imports System.Linq

Public Class Pre
    Public Property Wave As Integer
    Public Property IsMultiItem As Boolean
End Class

Public Module M
    Public Sub Do1(pps As List(Of Pre))
        Dim multiItemWaves = (From pp In pps Where pp.IsMultiItem Group By pp.Wave Into AsEnumerable Order By AsEnumerable.Count Descending).ToList
        Dim waves = multiItemWaves.Select(Function(w) w.Wave).ToList()
        For Each w In multiItemWaves
            Dim pps2 = w.AsEnumerable.ToList()
        Next
    End Sub
End Module",
            @"using System.Collections.Generic;
using System.Linq;

public partial class Pre
{
    public int Wave { get; set; }
    public bool IsMultiItem { get; set; }
}

public static partial class M
{
    public static void Do1(List<Pre> pps)
    {
        var multiItemWaves = (from pp in pps
                              where pp.IsMultiItem
                              group pp by pp.Wave into Group
                              let Wave = Group.Key
                              let AsEnumerable = Group.AsEnumerable()
                              orderby AsEnumerable.Count() descending
                              select new { Wave, AsEnumerable }).ToList();
        var waves = multiItemWaves.Select(w => w.Wave).ToList();
        foreach (var w in multiItemWaves)
        {
            var pps2 = w.AsEnumerable.ToList();
        }
    }
}");
    }

    [Fact(Skip = "TDD: CS1503 `TKey` → `Guid` (10 sites). Generic dictionary extension called on `Dictionary<Guid, T>` where TKey should bind Guid but codeconv drops type args")]
    public async Task DictionaryExtensionMethodGenericInferenceAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"", @"");
    }

    [Fact]
    public async Task ImplicitSelectProjectsAllRangeVariablesAsync()
    {
        // BMCore StockLevelDetail / CourierService: a VB query with multiple
        // From/Join/Let range variables and NO explicit Select produces the
        // transparent-identifier shape `{detail, sl, si}` — downstream code
        // does `d.sl.X` / `d.detail.X` / `item.dw`. The C# implicit select
        // previously picked only the first range variable (`select detail`),
        // so every downstream member access failed (CS1061 x20+).
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Collections.Generic
Imports System.Linq

Public Class Bz
    Public Property ID As Integer
End Class

Public Class Dw
    Public Property WeightG As Integer
End Class

Public Module M
    Public Sub Do1(bzs As List(Of Bz), dws As List(Of Dw))
        Dim q = From bz In bzs
                From dw In dws
        Dim heavy = q.Where(Function(item) item.dw.WeightG > 100 AndAlso item.bz.ID > 0).ToList()
    End Sub
End Module",
            @"using System.Collections.Generic;
using System.Linq;

public partial class Bz
{
    public int ID { get; set; }
}

public partial class Dw
{
    public int WeightG { get; set; }
}

public static partial class M
{
    public static void Do1(List<Bz> bzs, List<Dw> dws)
    {
        var q = from bz in bzs
                from dw in dws
                select new { bz, dw };
        var heavy = q.Where(item => item.dw.WeightG > 100 && item.bz.ID > 0).ToList();
    }
}");
    }

    [Fact]
    public async Task ImplicitSelectProjectsJoinAndLetVariablesAsync()
    {
        // Companion to ImplicitSelectProjectsAllRangeVariablesAsync: Join
        // (without Into) and Let also extend VB's transparent identifier.
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Collections.Generic
Imports System.Linq

Public Class Detail
    Public Property ID As Integer
End Class

Public Class Level
    Public Property ID As Integer
    Public Property Qty As Integer
End Class

Public Module M
    Public Sub Do1(details As List(Of Detail), levels As List(Of Level))
        Dim q = From detail In details
                Join sl In levels On sl.ID Equals detail.ID
                Let herald = sl.Qty * 2
                Where sl.Qty > 0
        Dim r = q.Select(Function(d) d.detail.ID + d.sl.Qty + d.herald).ToList()
    End Sub
End Module",
            @"using System.Collections.Generic;
using System.Linq;

public partial class Detail
{
    public int ID { get; set; }
}

public partial class Level
{
    public int ID { get; set; }
    public int Qty { get; set; }
}

public static partial class M
{
    public static void Do1(List<Detail> details, List<Level> levels)
    {
        var q = from detail in details
                join sl in levels on detail.ID equals sl.ID
                let herald = sl.Qty * 2
                where sl.Qty > 0
                select new { detail, sl, herald };
        var r = q.Select(d => d.detail.ID + d.sl.Qty + d.herald).ToList();
    }
}");
    }

    [Fact(Skip = "TDD: CS1503 `T` → concrete type (5 sites, WaveBuilder+UpdateStockItemPurchasePricesTask). Loop var in an untyped List<T> context can't add to concrete-typed list — needs type-constraint recovery from enclosing method")]
    public async Task GenericTLoopVarToConcreteListAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"", @"");
    }

    [Fact]
    public async Task NullableDecimalLambdaBodyIsUnwrappedForDecimalDelegateAsync()
    {
        // Extends the bool? lambda unwrap to numeric types. VB `Function(oi)
        // (From x In items Select If(cond, x.A, x.B)).Sum()` — the ternary
        // yields `decimal?` when either arm is nullable, `.Sum()` returns
        // `decimal?`, and the containing Expression<Func<..., decimal>> lambda
        // expects `decimal`. C# rejects `decimal?` → `decimal` (CS0266).
        //
        // Fix: unwrap the body with `?? default` when the target delegate
        // return type matches the body's underlying non-nullable type.
        // Works in expression-tree context too — EF translates `?? 0m`
        // cleanly.
        await TestConversionVisualBasicToCSharpAsync(@"Imports System
Imports System.Collections.Generic
Imports System.Linq

Public Class Item
    Public Property Amount As Decimal?
End Class

Public Module M
    Public Function Total(items As IEnumerable(Of Item)) As Decimal
        Dim getTotal As Func(Of IEnumerable(Of Item), Decimal) = Function(xs) xs.Sum(Function(x) x.Amount)
        Return getTotal(items)
    End Function
End Module",
            @"using System;
using System.Collections.Generic;
using System.Linq;

public partial class Item
{
    public decimal? Amount { get; set; }
}

public static partial class M
{
    public static decimal Total(IEnumerable<Item> items)
    {
        Func<IEnumerable<Item>, decimal> getTotal = xs => xs.Sum(x => x.Amount) ?? default;
        return getTotal(items);
    }
}");
    }

    [Fact]
    public async Task WhereClauseConditionalAccessBoolUnwrappedAsync()
    {
        // VB `Where r.Reason?.SomeBool` — the `?.` gives `bool?`, VB accepts
        // it via nullable Boolean semantics (Nothing → filter out). C#
        // `where` requires `bool` (CS0266). ConvertWhereClauseAsync now
        // appends `?? false` when the condition's inferred VB type is
        // `Nullable<Boolean>`, preserving VB's semantics.
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Collections.Generic
Imports System.Linq

Public Class Reason
    Public Property AutoCancel As Boolean
End Class

Public Class RRE
    Public Property Reason As Reason
End Class

Public Module M
    Public Function Any1(rres As IEnumerable(Of RRE)) As Boolean
        Return (From r In rres Where r.Reason?.AutoCancel Select r).Any()
    End Function
End Module",
            @"using System.Collections.Generic;
using System.Linq;

public partial class Reason
{
    public bool AutoCancel { get; set; }
}

public partial class RRE
{
    public Reason Reason { get; set; }
}

public static partial class M
{
    public static bool Any1(IEnumerable<RRE> rres)
    {
        return (from r in rres
                where (r.Reason?.AutoCancel) ?? false
                select r).Any();
    }
}");
    }

    [Fact(Skip = "TDD: CS0266 `string` → `SqlQueryWithParameters` (3 sites). Simple probe with a direct Narrowing CType on the exact target type ALREADY produces the correct explicit cast. The failing BMCore shape is subtler — the target is `SqlQueryWithParametersAndUIAlerts`, a subclass of the type carrying the CType(String) operator, so VB has to chain String→Base→Derived while C# needs both a user-defined cast and a downcast. Needs a subclass-aware repro before the fix can be scoped")]
    public async Task StringToSqlQueryWithParametersImplicitConversionAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"", @"");
    }

    [Fact]
    public async Task DoubleToNullableDecimalAssignmentAsync()
    {
        // VB `pol.PostageBand = 1.96` — RHS is Double (VB default for
        // decimal literals with no suffix), LHS is Decimal?. VB widens
        // implicitly. Codeconv preserves `1.96d` (C# double) and misses the
        // explicit cast in TypeConversionAnalyzer, giving CS0266.
        await TestConversionVisualBasicToCSharpAsync(@"Public Class Line
    Public Property Amount As Decimal?
End Class

Public Module M
    Public Sub Do1(l As Line)
        l.Amount = 1.96
    End Sub
End Module",
            @"
public partial class Line
{
    public decimal? Amount { get; set; }
}

public static partial class M
{
    public static void Do1(Line l)
    {
        l.Amount = 1.96m;
    }
}");
    }

    [Fact]
    public async Task IntToUShortNarrowingAsync()
    {
        // BMCore CompetitorFeedback CRC16 pattern. VB widens/narrows implicitly
        // between UShort and Integer for shift/bitwise ops. C# rejects storing
        // an int back into a ushort without a cast (CS0266). VB `CUShort(...)`
        // wrapping IS supposed to become `(ushort)(...)` — probe here shows what
        // codeconv actually emits.
        await TestConversionVisualBasicToCSharpAsync(@"Public Class C
    Public Function ComputeChecksum(bytes As Byte()) As UShort
        Dim crc As UShort = 0
        Dim table As UShort() = New UShort(255) {}
        For i As Integer = 0 To bytes.Length - 1
            crc = CUShort((crc << 8) Xor table(((crc >> 8) Xor (&HFF And bytes(i)))))
        Next
        Return crc
    End Function
End Class",
            @"
public partial class C
{
    public ushort ComputeChecksum(byte[] bytes)
    {
        ushort crc = 0;
        ushort[] table = new ushort[256];
        for (int i = 0, loopTo = bytes.Length - 1; i <= loopTo; i++)
            crc = (ushort)((ushort)(crc << 8) ^ table[crc >> 8 ^ 0xFF & bytes[i]]);
        return crc;
    }
}");
    }

    [Fact(Skip = "TDD: CS0266 `IQueryable<T>` → `DbSet<T>` (2 sites). Codeconv preserves .Where() result assigned back to a DbSet-typed variable — need to reassign as IQueryable or peel the .Where")]
    public async Task QueryableAssignedBackToDbSetAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"", @"");
    }

    [Fact(Skip = "TDD: CS1662 remaining lambda-return sites (15). Non-Where/Any predicates that still emit bool? bodies for a bool delegate — likely OrderBy/GroupBy key selectors and similar")]
    public async Task RemainingLambdaReturnTypeMismatchAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"", @"");
    }

    [Fact]
    public async Task DecimalPlusDoubleInCompoundExpressionCastsBothToFloatAsync()
    {
        // Complement to DecimalPlusDoublePromotesToDecimalAsync: the simple
        // `Return a + b` case with (decimal, double) return type is already
        // handled by the top-level assignment analyzer. But nested usages
        // like `Total = Total + Conversions.ToDouble(c.Value)` (inside a
        // larger expression tree) don't get the operator-level widening —
        // the outer `(decimal)(...)` cast is added but the inner `decimal +
        // double` still fails with CS0019.
        //
        // Fix at VisitBinaryExpression: when an arithmetic op (+, -, *, /,
        // %) mixes decimal with double/single, force both operands to the
        // wider floating type. Outer conversion casts the result back to
        // decimal.
        //
        // Clears the CS0019 `decimal + double` cluster in ProvisionReport-
        // Model, ProfitCalculator.PostageAndPackaging, etc. (~14 sites).
        await TestConversionVisualBasicToCSharpAsync(@"Public Module M
    Public Sub Do1()
        Dim Total As Decimal = 0
        Total = Total + CDbl(""1.5"")
    End Sub
End Module",
            @"using Microsoft.VisualBasic.CompilerServices; // Install-Package Microsoft.VisualBasic

public static partial class M
{
    public static void Do1()
    {
        decimal Total = 0m;
        Total = (decimal)((double)Total + Conversions.ToDouble(""1.5""));
    }
}");
    }

    [Fact]
    public async Task AddressOfMatchingArityWithNullableWideningForwardsArgsAsync()
    {
        // VB `AddressOf SetX(decimal?)` bound to `Action<decimal>` — arities
        // match (1 param each) but signature-check fails because delegate
        // provides `decimal` while method wants `decimal?`. VB permits the
        // widening; C# doesn't allow the method-group conversion, so
        // codeconv needs to wrap in a lambda.
        //
        // Previously `ThrowawayParameters` was used which DISCARDED the arg,
        // producing `(_) => setter()` — CS7036 "no argument given for
        // required parameter 'value'". Now the arity check distinguishes
        // FEWER-params (throwaway, e.g. `AddressOf Foo()` → EventHandler)
        // from MATCHING-arity (forward args through the widening).
        //
        // Cleared 4+ CS7036 sites at GetAmazonDimensionsTask —
        // SetAmazonHeightInches / Width / Depth / WeightLB.
        await TestConversionVisualBasicToCSharpAsync(@"Public Class Item
    Public Sub SetX(value As Decimal?)
    End Sub
End Class

Public Module M
    Public Sub Do1(item As Item, setter As Action(Of Decimal))
        setter = AddressOf item.SetX
    End Sub
End Module",
            @"using System;

public partial class Item
{
    public void SetX(decimal? value)
    {
    }
}

public static partial class M
{
    public static void Do1(Item item, Action<decimal> setter)
    {
        setter = (arg1) => item.SetX(arg1);
    }
}");
    }

    [Fact]
    public async Task CompositeGroupByKeyLetBindsEachImplicitNameAsync()
    {
        // VB `Group By oipi.StockItem, oipi.WarehouseLocation Into Sum = ...`
        // — the composite key becomes a C# anonymous type via `@group.Key`.
        // Downstream code references bare `StockItem` and `WarehouseLocation`
        // (VB transparent identifier), but C# needs `@group.Key.StockItem`
        // etc. — bare references treat them as TYPE names, giving CS0119
        // "'StockItem' is a type, which is not valid in the given context".
        //
        // The single-key case was already handled (`let X = @group.Key`).
        // Extend to composite: emit `let <keyName> = @group.Key.<keyName>`
        // for each implicitly-named key.
        //
        // Clears CS0119 sites in Picking/Wave (`StockItem`, `WarehouseLocation`)
        // and similar composite-key patterns across BMCore.
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Collections.Generic
Imports System.Linq

Public Class StockItem
    Public Property ID As Integer
End Class

Public Class WarehouseLocation
    Public Property ID As Integer
End Class

Public Class Item
    Public Property Stock As StockItem
    Public Property Location As WarehouseLocation
    Public Property Qty As Integer
End Class

Public Module M
    Public Function Do1(items As IEnumerable(Of Item)) As Integer
        Return (From x In items
                Group By x.Stock, x.Location Into Total = Sum(x.Qty)
                Select New With {Stock, Location, Total}).Count()
    End Function
End Module",
            @"using System.Collections.Generic;
using System.Linq;

public partial class StockItem
{
    public int ID { get; set; }
}

public partial class WarehouseLocation
{
    public int ID { get; set; }
}

public partial class Item
{
    public StockItem Stock { get; set; }
    public WarehouseLocation Location { get; set; }
    public int Qty { get; set; }
}

public static partial class M
{
    public static int Do1(IEnumerable<Item> items)
    {
        return (from x in items
                group x by new { x.Stock, x.Location } into Group
                let Stock = Group.Key.Stock
                let Location = Group.Key.Location
                let Total = Group.Sum(x => x.Qty)
                select new { Stock, Location, Total }).Count();
    }
}");
    }

    [Fact]
    public async Task ShortLambdaBodyInPredicateContextUnwrapsWithZeroCheckAsync()
    {
        // VB `.Any(Function(x) x.SomeShort)` — VB accepts truthy numeric
        // in a bool context (non-zero = true). C# rejects (CS0029 short →
        // bool). Same pattern as the enum-in-Where fix but at the Function
        // lambda body level for predicate delegates like Any/All/Where.
        //
        // Clears CS0029 short → bool at WavePrioritiser (and any similar
        // truthy-numeric predicate).
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Collections.Generic
Imports System.Linq

Public Class Item
    Public Property QtyScanned As Short
End Class

Public Module M
    Public Function CountScanned(items As IEnumerable(Of Item)) As Integer
        Return items.Where(Function(x) x.QtyScanned).Count()
    End Function
End Module",
            @"using System.Collections.Generic;
using System.Linq;

public partial class Item
{
    public short QtyScanned { get; set; }
}

public static partial class M
{
    public static int CountScanned(IEnumerable<Item> items)
    {
        return items.Where(x => x.QtyScanned != 0).Count();
    }
}");
    }

    [Fact]
    public async Task WhereClauseEnumBitwiseUnwrapsWithZeroCheckAsync()
    {
        // VB `Where t.Role And Server.Role` (bitwise `And` on flag-enums)
        // returns an enum. VB accepts enum in Where via non-zero-is-true
        // semantics. C# `where` requires `bool` (CS0029).
        //
        // Fix in ConvertWhereClauseAsync: when the condition's inferred type
        // is an enum, emit `((<underlying>)condition) != 0`. Cast to the
        // underlying integer type first so the `0` literal comparison
        // resolves without needing an `(EnumType)0` literal.
        //
        // Clears CS0029 enum→bool sites in HeartBeat.
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Collections.Generic
Imports System.Linq

<System.Flags>
Public Enum Role
    None = 0
    Main = 1
    Test = 2
End Enum

Public Class Task
    Public Property MyRole As Role
End Class

Public Module M
    Public Function Do1(tasks As IEnumerable(Of Task), r As Role) As Integer
        Return (From t In tasks Where t.MyRole And r Select t).Count()
    End Function
End Module",
            @"using System;
using System.Collections.Generic;
using System.Linq;

[Flags]
public enum Role
{
    None = 0,
    Main = 1,
    Test = 2
}

public partial class Task
{
    public Role MyRole { get; set; }
}

public static partial class M
{
    public static int Do1(IEnumerable<Task> tasks, Role r)
    {
        return (from t in tasks
                where (t.MyRole & r) != 0
                select t).Count();
    }
}");
    }

    [Fact(Skip = "TDD: CS0029 Thread[] → ScrapingFakeMachine[] (AmazonScraper). Array covariance/downcast mismatch — VB permits, C# needs explicit cast per element")]
    public async Task ArrayCovariantDowncastAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"", @"");
    }

    [Fact(Skip = "TDD: CS0411 `TryGetIEnumerableOrEmpty<TKey,TElem>` inference (5 sites). Simple probe with dict + query works — the actual failing shape has a more complex receiver (probably chained through GroupIntoDictionary / an anon-typed source). Need to construct a repro from the actual emission")]
    public async Task GenericExtensionMethodTypeArgsExplicitAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"", @"");
    }

    [Fact]
    public async Task NestedGroupByUniqueGroupIdentifierAsync()
    {
        // VB `Group By Group = wd.Date Into AsEnumerable` — the key alias
        // `Group` collides with codeconv's default group identifier `Group`
        // (used for the C# `into Group` keyword). Emission was `into Group
        // let Group = Group.Key` which fails CS1930 "range variable already
        // declared".
        //
        // Fix in GetGroupIdentifier: skip any candidate identifier that
        // matches a key name we'll subsequently let-bind, falling back to
        // `@group`.
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Collections.Generic
Imports System.Linq

Public Class Wave
    Public Property Date_ As System.DateTime
End Class

Public Module M
    Public Sub Do1()
        Dim waves As New List(Of Wave)
        Dim r = (From wd In waves
                 Group By Group = wd.Date_ Into AsEnumerable
                 Order By Group).ToList()
    End Sub
End Module",
            @"using System;
using System.Collections.Generic;
using System.Linq;

public partial class Wave
{
    public DateTime Date_ { get; set; }
}

public static partial class M
{
    public static void Do1()
    {
        var waves = new List<Wave>();
        var r = (from wd in waves
                 group wd by wd.Date_ into @group
                 let Group = @group.Key
                 let AsEnumerable = @group.AsEnumerable()
                 orderby Group
                 select new { Group, AsEnumerable }).ToList();
    }
}");
    }

    [Fact]
    public async Task JoinEqualsOperandOrderSwapWithDeepMemberAccessAsync()
    {
        // VB permits either operand of `Equals` to reference the new join
        // variable; C# requires the LEFT to reference outer scope and the
        // RIGHT to reference the new join variable (CS1937/1938 otherwise).
        //
        // Codeconv already swapped for bare `newVar` and one-deep
        // `newVar.Member`, but MISSED deep chains like `newVar.Sub.Member`
        // — the check only inspected the immediate MemberAccess's Expression.
        // Fix: walk the chain to the root identifier.
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Collections.Generic
Imports System.Linq

Public Class Sl
    Public Property StockItemID As Integer
End Class

Public Class Oi
    Public Property StockItemID As Integer
End Class

Public Class RreItem
    Public Property OrderItem As Oi
End Class

Public Module M
    Public Sub Do1(sls As IEnumerable(Of Sl), rreitems As IEnumerable(Of RreItem))
        Dim r = From sl In sls
                Join rreitem In rreitems On rreitem.OrderItem.StockItemID Equals sl.StockItemID
                Select sl
    End Sub
End Module",
            @"using System.Collections.Generic;
using System.Linq;

public partial class Sl
{
    public int StockItemID { get; set; }
}

public partial class Oi
{
    public int StockItemID { get; set; }
}

public partial class RreItem
{
    public Oi OrderItem { get; set; }
}

public static partial class M
{
    public static void Do1(IEnumerable<Sl> sls, IEnumerable<RreItem> rreitems)
    {
        var r = from sl in sls
                join rreitem in rreitems on sl.StockItemID equals rreitem.OrderItem.StockItemID
                select sl;
    }
}");
    }

    [Fact(Skip = "TDD: CS1936 no query pattern on TValue (2 sites). Generic type argument used as query source — needs interface constraint check or fallback")]
    public async Task GenericTValueAsQuerySourceAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"", @"");
    }

    [Fact]
    public async Task ForEachIterationVariableReassignmentAsync()
    {
        // VB `For Each ltr In items ... ltr = ltr.Trim() ...` — VB permits
        // reassigning a For Each control variable; C# `foreach` doesn't
        // (CS1656). LocalVariableAnalyzer must exclude the variable from
        // its "inline" set when the loop body assigns to it, so the visitor
        // falls into the `currentX` alias branch and emits a mutable local
        // shadow.
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Collections.Generic
Imports System.Linq

Public Module M
    Public Sub Do1(items As String())
        For Each ltr In items
            ltr = ltr.Trim()
            If ltr.Length > 0 Then Continue For
        Next
    End Sub
End Module",
            @"
public static partial class M
{
    public static void Do1(string[] items)
    {
        foreach (var currentLtr in items)
        {
            var ltr = currentLtr;
            ltr = ltr.Trim();
            if (ltr.Length > 0)
                continue;
        }
    }
}", incompatibleWithAutomatedCommentTesting: true);
    }

    [Fact]
    public async Task ConditionalMethodDelegateWrapAsync()
    {
        // VB `AddressOf Debug.WriteLine` permitted a method-group conversion
        // to a delegate for a method marked [Conditional("DEBUG")]. C#
        // forbids that (CS1618) because the delegate could be invoked in a
        // Release build where the method would silently no-op.
        //
        // Fix: emit a lambda wrapper `(arg1) => Debug.WriteLine(arg1)`. The
        // method call inside the lambda body is honoured like any other call
        // (elided in Release when the condition symbol isn't defined) —
        // matches VB's runtime behaviour.
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Diagnostics

Public Module M
    Public Sub Do1(setter As Action(Of String))
        setter = AddressOf Debug.WriteLine
    End Sub
End Module",
            @"using System;
using System.Diagnostics;

public static partial class M
{
    public static void Do1(Action<string> setter)
    {
        setter = (arg1) => Debug.WriteLine(arg1);
    }
}");
    }

    [Fact]
    public async Task FieldInitializerReferencingInstanceMemberAsync()
    {
        // BMCore LBoardConfig has `Public Property Mins As Integer = MsProp / 60000`
        // where MsProp is a computed instance property. VB runs property
        // initializers inside the constructor so this is fine. C# property
        // initializers are static-only (CS0236). Fix: hoist the initializer
        // into an instance ctor assignment and drop the `= expr` on the
        // auto-property declaration.
        await TestConversionVisualBasicToCSharpAsync(@"Public Class LBoardConfig
    Public ReadOnly Property RefreshTimeMS As Integer
        Get
            Return RefreshTimeMins * 60000
        End Get
    End Property
    Public Property RefreshTimeMins As Integer = RefreshTimeMS / 60000
End Class",
            @"using System;

public partial class LBoardConfig
{
    public int RefreshTimeMS
    {
        get
        {
            return RefreshTimeMins * 60000;
        }
    }
    public int RefreshTimeMins { get; set; }

    public LBoardConfig()
    {
        RefreshTimeMins = (int)Math.Round(RefreshTimeMS / 60000d);
    }
}");
    }

    [Fact]
    public async Task SingleItemSelectRenamesOuterRangeVarAsync()
    {
        // VB `From sl In src Select sl.WarehouseLocationID Where WarehouseLocationID.HasValue`
        // — the single-item Select renames the range variable to
        // `WarehouseLocationID` (VB implicit name from the member access).
        // Subsequent clauses in the same query reference `WarehouseLocationID`
        // directly.
        //
        // Codeconv emits the inner Select correctly but the OUTER segment's
        // `from` still uses the original `sl` name, so the bare
        // `WarehouseLocationID` reference in Where/Select becomes CS0117 /
        // CS0103 (compiler mistakes `WarehouseLocationID` for a type name).
        //
        // Fix: when a segment's queryEnd is a single-item Select whose item
        // has an implicit or explicit name, use THAT name for the next
        // segment's FromClause range variable.
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Collections.Generic
Imports System.Linq

Public Class Loc
    Public Property WarehouseLocationID As Integer?
End Class

Public Module M
    Public Sub Do1()
        Dim locs As New List(Of Loc)
        Dim ids = (From sl In locs
                   Select sl.WarehouseLocationID
                   Where WarehouseLocationID.HasValue
                   Select WarehouseLocationID.Value).Distinct().ToArray()
    End Sub
End Module",
            @"using System.Collections.Generic;
using System.Linq;

public partial class Loc
{
    public int? WarehouseLocationID { get; set; }
}

public static partial class M
{
    public static void Do1()
    {
        var locs = new List<Loc>();
        int[] ids = (from WarehouseLocationID in
                         from sl in locs
                         select sl.WarehouseLocationID
                     where WarehouseLocationID.HasValue
                     select WarehouseLocationID.Value).Distinct().ToArray();
    }
}");
    }

    [Fact]
    public async Task SelfReferenceCheckIgnoresQualifierSharingLocalNameAsync()
    {
        // The CS0165 self-referential-lambda fix must not misfire when the
        // initializer references a TYPE or NAMESPACE that shares its final
        // identifier with the local being declared:
        //   Dim MarketPlace = Api.MarketPlace.GetById(...)
        // Text-only name matching would treat `MarketPlace` under `Api.` as a
        // self-reference and split into `... = default; ... = <init>;` —
        // giving CS8716 "There is no target type for the default literal".
        //
        // Fix uses semantic symbol resolution to confirm the identifier binds
        // to the declared local (ILocalSymbol) before triggering the split.
        await TestConversionVisualBasicToCSharpAsync(@"Public Class Api
    Public Class MarketPlace
        Public Shared Function GetById(id As Integer) As String
            Return """"
        End Function
    End Class
End Class

Public Module M
    Public Sub Do1(id As Integer)
        Dim MarketPlace = Api.MarketPlace.GetById(id)
    End Sub
End Module",
            @"
public partial class Api
{
    public partial class MarketPlace
    {
        public static string GetById(int id)
        {
            return """";
        }
    }
}

public static partial class M
{
    public static void Do1(int id)
    {
        string MarketPlace = Api.MarketPlace.GetById(id);
    }
}");
    }

    [Fact]
    public async Task SelfReferentialLambdaSplitsIntoDeclareThenAssignAsync()
    {
        // VB `Dim rec = Function(x) rec(x - 1)` works because VB implicitly
        // initialises `rec` to Nothing before evaluating the initializer.
        // C# requires definite assignment before use — the `rec(x - 1)` in
        // the lambda body would fire CS0165 "use of unassigned local".
        //
        // Fix: split into `T rec = default; rec = <init>;` so the lambda
        // body's self-reference sees a definitely-assigned local.
        await TestConversionVisualBasicToCSharpAsync(@"Imports System

Public Module M
    Public Sub Do1()
        Dim rec As Action(Of Integer) = Sub(x)
                                            If x > 0 Then rec(x - 1)
                                        End Sub
        rec(3)
    End Sub
End Module",
            @"using System;

public static partial class M
{
    public static void Do1()
    {
        Action<int> rec = default;
        rec = x => { if (x > 0) rec(x - 1); };
        rec(3);
    }
}");
    }

    [Fact]
    public async Task ForEachReusingOuterLocalDoesntRedeclareAsync()
    {
        // VB `For Each x In xs` where `x` is ALREADY declared as an outer
        // local in the same method REUSES that outer local (VB semantics).
        // My CS1656 fix (foreach control-variable shadow) was emitting
        // `var x = currentX;` inside the loop body which then collides
        // with the outer `x` (CS0136 "cannot be declared in this scope
        // because that name is used in an enclosing local scope").
        //
        // Fix: when varSymbol's declaring syntax is OUTSIDE the loop block
        // (i.e. VB re-uses an outer local), emit a bare assignment `x =
        // currentX;` instead of a declaration. The outer local is what
        // the loop body's references resolve to.
        //
        // Clears CS0136 sites in CombinedMapBuilder (`room`), Order.Amazon
        // (`i`), and ChannelSKU (`ItemPrice`).
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Collections.Generic
Imports System.Linq

Public Module M
    Public Sub Do1(items As List(Of Integer))
        Dim x = items.First()
        System.Console.WriteLine(x)
        For Each x In items
            Dim y = x + 1
        Next
        System.Console.WriteLine(x)
    End Sub
End Module",
            @"using System;
using System.Collections.Generic;
using System.Linq;

public static partial class M
{
    public static void Do1(List<int> items)
    {
        int x = items.First();
        Console.WriteLine(x);
        foreach (var currentX in items)
        {
            x = currentX;
            int y = x + 1;
        }
        Console.WriteLine(x);
    }
}");
    }

    [Fact(Skip = "TDD: CS0030 Func<T, bool?> → Func<T, bool> (3 sites, DispatchScheduleRule). Passing an outer-nullable-bool predicate to a bool-Func parameter — needs unwrap wrapper lambda")]
    public async Task NullableBoolFuncToBoolFuncAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"", @"");
    }

    [Fact]
    public async Task ForEachBodyWithSingleDeclarationKeepsBracesAsync()
    {
        // VB `For Each o In orders : Dim od = o.Detail : Next` — VB permits a
        // single Dim as the loop body. Codeconv previously unpacked the
        // block wrap and emitted `foreach (var o in orders) var od = o.Detail;`
        // which C# rejects (CS1023: "Embedded statement cannot be a
        // declaration or labeled statement").
        //
        // Fix: UnpackNonNestedBlock keeps the block when the single statement
        // is a LocalDeclarationStatement or LabeledStatement.
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Collections.Generic

Public Class OrderRef
    Public Property Detail As Object
End Class

Public Module M
    Public Sub Do1(orders As IEnumerable(Of OrderRef))
        For Each o In orders
            Dim od = o.Detail
        Next
    End Sub
End Module",
            @"using System.Collections.Generic;

public partial class OrderRef
{
    public object Detail { get; set; }
}

public static partial class M
{
    public static void Do1(IEnumerable<OrderRef> orders)
    {
        foreach (var o in orders)
        {
            var od = o.Detail;
        }
    }
}");
    }

    [Fact(Skip = "TDD: CS1003 syntax error (4 sites). Investigate individually")]
    public async Task RemainingSyntaxErrorsAsync()
    {
        await TestConversionVisualBasicToCSharpAsync(@"", @"");
    }

    [Fact]
    public async Task TransparentSelectSkipsWhenDownstreamUsesSelfMemberAsync()
    {
        // Prevents a regression from the transparent-Select fix: VB
        // `Select oa, oa.Order, Weight = ...` creates an anon type with
        // `oa` AS A MEMBER (the OrderAction). Downstream code may access
        // `oa.oa` (VB transparent-identifier reference to the OrderAction
        // sub-member). If we let-emit and preserve `oa` as the OrderAction
        // range variable directly, `oa.oa` no longer resolves — CS1061.
        //
        // Fix skips the transform when the enclosing method contains any
        // `<bareName>.<bareName>` member access. Fall back to the default
        // anon-type projection which preserves both the range-var-as-member
        // shape and the outer scope's ability to access `.<name>`.
        //
        // Regression source: BMCore/Server/SetOrderPostalServicesInLinnworks.cs
        // (7 CS1061 sites), SetOrderPackagingGroupsInLinnworks (similar),
        // and a handful of other OrderActions sites.
        await TestConversionVisualBasicToCSharpAsync(@"Imports System.Collections.Generic
Imports System.Linq

Public Class Order
    Public Property ID As Integer
End Class

Public Class OrderAction
    Public Property Order As Order
    Public Property Done As System.DateTime?
End Class

Public Module M
    Public Function Do1(actions As IEnumerable(Of OrderAction)) As Integer
        Dim oas = (From oa In actions
                   Where oa.Order IsNot Nothing
                   Select oa, oa.Order, Weight = 1).ToList
        Dim r = oas.Where(Function(oa)
                              oa.oa.Done = System.DateTime.Now
                              Return True
                          End Function).ToList
        Return r.Count
    End Function
End Module",
            @"using System;
using System.Collections.Generic;
using System.Linq;

public partial class Order
{
    public int ID { get; set; }
}

public partial class OrderAction
{
    public Order Order { get; set; }
    public DateTime? Done { get; set; }
}

public static partial class M
{
    public static int Do1(IEnumerable<OrderAction> actions)
    {
        var oas = (from oa in actions
                   where oa.Order != null
                   select new { oa, oa.Order, Weight = 1 }).ToList();
        var r = oas.Where(oa =>
        {
            oa.oa.Done = DateTime.Now;
            return true;
        }).ToList();
        return r.Count;
    }
}");
    }
}