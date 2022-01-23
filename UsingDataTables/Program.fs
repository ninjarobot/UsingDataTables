open System
open System.Collections.Generic
open Azure.Identity
open Microsoft.Extensions.Configuration
open OpenTelemetry.Trace
open Azure
open Azure.Data.Tables

type OfficeSupply () =
    interface ITableEntity with
        member this.PartitionKey 
            with get() = this.PartitionKey
            and set value = this.PartitionKey <- value
        member this.RowKey
            with get() = this.RowKey
            and set value = this.RowKey <- value
        member this.ETag
            with get() = this.ETag
            and set value = this.ETag <- value
        member this.Timestamp
            with get() = this.Timestamp
            and set value = this.Timestamp <- value
    member val PartitionKey = Unchecked.defaultof<_> with get, set
    member val RowKey = Unchecked.defaultof<_> with get, set
    member val ETag = ETag() with get, set
    member val Timestamp = Nullable() with get, set
    member val Price = 0.0 with get, set

// Enable OpenTelemetry instrumentation so we can see the requests.
let traceProvider =
    OpenTelemetry.Sdk.CreateTracerProviderBuilder()
        .AddHttpClientInstrumentation()
        .AddConsoleExporter().Build()

let config =
    ConfigurationBuilder()
        .AddUserSecrets<OfficeSupply>()
        .Build()

// Connection strings and SAS tokens are supported as usual.
let tableServiceClient = TableServiceClient(config.GetConnectionString("AzureTables"))

// But it also supports Azure.Identity for Azure RBAC to get access to the tables.
// Using Azure RBAC requires the 'Storage Table Data Contributor' role (currently preview).
let tableServiceClientIdentity = TableServiceClient(Uri "https://mystorageaccount.table.core.windows.net", DefaultAzureCredential())
let tableName = "OfficeSupplies"
let response = tableServiceClient.CreateTableIfNotExists tableName

let officeSupplyTable = tableServiceClient.GetTableClient(tableName)

// Create some entities
let markers = OfficeSupply(PartitionKey="Writing", RowKey="Markers", Price=5.99)
let pens = OfficeSupply(PartitionKey="Writing", RowKey="Pens", Price=2.99)

// And they can be added with an Add or Upsert.
// These operations throw on error, so usually the response can be ignored.
officeSupplyTable.UpsertEntity markers |> ignore<Response>
officeSupplyTable.UpsertEntity pens |> ignore<Response>

// As well as within a transaction.
let transaction = ResizeArray<TableTransactionAction>()
transaction.Add(TableTransactionAction(TableTransactionActionType.UpsertReplace, markers))
transaction.Add(TableTransactionAction(TableTransactionActionType.UpsertReplace, pens))

// Transactions throw as well, so not much point in looking at the Azure response.
try
    officeSupplyTable.SubmitTransaction(transaction) |> ignore<Response<IReadOnlyList<Response>>>
with :? TableTransactionFailedException as ex ->
    // although if it fails, the transaction exception is interesting:
    let failedEntity:ITableEntity option =
        ex.FailedTransactionActionIndex
        |> Option.ofNullable
        |> Option.map (fun idx -> transaction.[idx].Entity)
    eprintfn $"Transaction failed with '{ex.Message}' for {failedEntity}"

// Writing an OData expression for filtering is supported
let suppliesOData = officeSupplyTable.Query<OfficeSupply>(filter="Price lt 5.00")
for s in suppliesOData do
    printfn $"String Filter - Category: '{ s.PartitionKey }' Product: '{ s.RowKey }' Price: {s.Price}"

// Better yet, lambdas are supported for type-safe filtering:
let suppliesLambda = officeSupplyTable.Query<OfficeSupply>(
    filter = (fun (officeSupply:OfficeSupply) ->
        officeSupply.Price < 5.0 && officeSupply.Price > 2.0)
)
for s in suppliesLambda do
    printfn $"Lambda Filter - Category: '{ s.PartitionKey }' Product: '{ s.RowKey }' Price: {s.Price}"

// F# query expressions work but are just filtered client side (check http.url in telemetry)
let suppliesQueryEx =
    query {
        for officeSupply in officeSupplyTable.Query<OfficeSupply>() do
        where (officeSupply.Price < 5.0) }
for s in suppliesQueryEx do
    printfn $"Query Expr Filter - Category: '{ s.PartitionKey }' Product: '{ s.RowKey }' Price: {s.Price}"
