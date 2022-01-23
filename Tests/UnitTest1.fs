module Tests

open System
open Azure
open Azure.Data.Tables
open NUnit.Framework
open Moq
open Program

// Azure.Data.Tables are quite a bit easier to mock than older storage table libraries.

let mockTableClient = Mock<TableClient>()

[<SetUp>]
let Setup () =
    let officeSupplies = [ OfficeSupply(PartitionKey="foo", RowKey="bar", Price=9.99) ]
    let pages = Pageable.FromPages [ Page.FromValues(officeSupplies, null, Mock.Of<Response>()) ]
    mockTableClient.Setup(fun tableClient -> tableClient.Query<OfficeSupply>()).Returns(pages) |> ignore
    mockTableClient.Setup(fun tableClient -> tableClient.Query<OfficeSupply>(It.IsAny<Linq.Expressions.Expression<Func<OfficeSupply,bool>>>())).Returns(pages) |> ignore

[<Test>]
let Test1 () =
    let tableClient = mockTableClient.Object
    let results = tableClient.Query<OfficeSupply>()
    for officeSupply in results do
        Assert.AreEqual (9.99, officeSupply.Price)
    Assert.Pass()

[<Test>]
let Test2 () =
    let tableClient = mockTableClient.Object
    let numResults = tableClient.Query<OfficeSupply>() |> Seq.length
    Assert.AreEqual(1, numResults)
