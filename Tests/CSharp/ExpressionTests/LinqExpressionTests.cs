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
                         select Group;
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
                     select @group;
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
        listSelectWhere = from t in
                              from t in l
                              select t.foo
                          where 1 == 2
                          select t;
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
}