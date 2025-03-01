[<AutoOpen>]
module Farmer.Arm.DocumentDb

open Farmer
open Farmer.CosmosDb

let containers =
    ResourceType("Microsoft.DocumentDb/databaseAccounts/sqlDatabases/containers", "2021-04-15")

let sqlDatabases =
    ResourceType("Microsoft.DocumentDb/databaseAccounts/sqlDatabases", "2021-04-15")

let mongoDatabases =
    ResourceType("Microsoft.DocumentDb/databaseAccounts/mongodbDatabases", "2021-04-15")

let databaseAccounts =
    ResourceType("Microsoft.DocumentDb/databaseAccounts", "2021-04-15")

let gremlinDatabases =
    ResourceType("Microsoft.DocumentDb/databaseAccounts/gremlinDatabases", "2022-05-15")

let graphs =
    ResourceType("Microsoft.DocumentDb/databaseAccounts/gremlinDatabases/graphs", "2022-05-15")

type DatabaseKind =
    | Document
    | Mongo
    | Gremlin

module DatabaseAccounts =
    module Containers =
        type Container = {
            Name: ResourceName
            Account: ResourceName
            Database: ResourceName
            Kind: DatabaseKind
            PartitionKey: {|
                Paths: string list
                Kind: IndexKind
            |}
            UniqueKeyPolicy: {|
                UniqueKeys: {| Paths: string list |} Set
            |}
            IndexingPolicy: {|
                IncludedPaths:
                    {|
                        Path: string
                        Indexes: (IndexDataType * IndexKind) list
                    |} list
                ExcludedPaths: string list
            |}
        } with

            interface IArmResource with
                member this.ResourceId =
                    match this.Kind with
                    | Gremlin -> graphs.resourceId (this.Account / this.Database / this.Name)
                    | _ -> containers.resourceId (this.Account / this.Database / this.Name)

                member this.JsonModel =
                    let resourceType =
                        match this.Kind with
                        | Gremlin -> graphs
                        | _ -> containers

                    {|
                        resourceType.Create(
                            this.Account / this.Database / this.Name,
                            dependsOn = [
                                match this.Kind with
                                | Gremlin -> gremlinDatabases.resourceId (this.Account, this.Database)
                                | _ -> sqlDatabases.resourceId (this.Account, this.Database)
                            ]
                        ) with
                            properties = {|
                                resource = {|
                                    id = this.Name.Value
                                    partitionKey = {|
                                        paths = this.PartitionKey.Paths
                                        kind = string this.PartitionKey.Kind
                                    |}
                                    uniqueKeyPolicy = {|
                                        uniqueKeys =
                                            this.UniqueKeyPolicy.UniqueKeys |> Set.map (fun k -> {| paths = k.Paths |})
                                    |}
                                    indexingPolicy = {|
                                        indexingMode = "consistent"
                                        includedPaths =
                                            this.IndexingPolicy.IncludedPaths
                                            |> List.map (fun p -> {|
                                                path = p.Path
                                                indexes =
                                                    p.Indexes
                                                    |> List.map (fun (dataType, kind) -> {|
                                                        kind = string kind
                                                        dataType = dataType.ToString().ToLower()
                                                        precision = -1
                                                    |})
                                            |})
                                        excludedPaths =
                                            this.IndexingPolicy.ExcludedPaths |> List.map (fun p -> {| path = p |})
                                    |}
                                |}
                            |}
                    |}

    type SqlDatabase = {
        Name: ResourceName
        Account: ResourceName
        Throughput: Throughput
        Kind: DatabaseKind
    } with

        interface IArmResource with
            member this.ResourceId =
                match this.Kind with
                | Gremlin -> gremlinDatabases.resourceId (this.Account / this.Name)
                | _ -> sqlDatabases.resourceId (this.Account / this.Name)

            member this.JsonModel =
                let resource =
                    match this.Kind with
                    | Document -> sqlDatabases
                    | Mongo -> mongoDatabases
                    | Gremlin -> gremlinDatabases

                {|
                    resource.Create(this.Account / this.Name, dependsOn = [ databaseAccounts.resourceId this.Account ]) with
                        properties = {|
                            resource = {| id = this.Name.Value |}
                            options = {|
                                throughput =
                                    match this.Throughput with
                                    | Provisioned t -> string t
                                    | Serverless -> null
                            |}
                        |}
                |}

type DatabaseAccount = {
    Name: ResourceName
    Location: Location
    ConsistencyPolicy: ConsistencyPolicy
    FailoverPolicy: FailoverPolicy
    PublicNetworkAccess: FeatureFlag
    FreeTier: bool
    Serverless: FeatureFlag
    Kind: DatabaseKind
    Tags: Map<string, string>
} with

    member this.MaxStatelessPrefix =
        match this.ConsistencyPolicy with
        | BoundedStaleness(staleness, _) -> Some staleness
        | Session
        | Eventual
        | ConsistentPrefix
        | Strong -> None

    member this.MaxInterval =
        match this.ConsistencyPolicy with
        | BoundedStaleness(_, interval) -> Some interval
        | Session
        | Eventual
        | ConsistentPrefix
        | Strong -> None

    member this.EnableAutomaticFailover =
        match this.FailoverPolicy with
        | AutoFailover _ -> Some true
        | _ -> None

    member this.EnableMultipleWriteLocations =
        match this.FailoverPolicy with
        | MultiMaster _ -> Some true
        | _ -> None

    member this.FailoverLocations = [
        match this.FailoverPolicy with
        | AutoFailover secondary
        | MultiMaster secondary ->
            {|
                LocationName = this.Location.ArmValue
                FailoverPriority = 0
            |}

            {|
                LocationName = secondary.ArmValue
                FailoverPriority = 1
            |}
        | NoFailover -> ()
    ]

    interface IArmResource with
        member this.ResourceId = databaseAccounts.resourceId this.Name

        member this.JsonModel = {|
            databaseAccounts.Create(this.Name, this.Location, tags = this.Tags) with
                kind =
                    match this.Kind with
                    | Document -> "GlobalDocumentDB"
                    | Mongo -> "MongoDB"
                    | Gremlin -> "GlobalDocumentDB"
                properties =
                    {|
                        consistencyPolicy = {|
                            defaultConsistencyLevel =
                                match this.ConsistencyPolicy with
                                | BoundedStaleness _ -> "BoundedStaleness"
                                | Session
                                | Eventual
                                | ConsistentPrefix
                                | Strong as policy -> string policy
                            maxStalenessPrefix = this.MaxStatelessPrefix |> Option.toNullable
                            maxIntervalInSeconds = this.MaxInterval |> Option.toNullable
                        |}
                        databaseAccountOfferType = "Standard"
                        enableAutomaticFailover = this.EnableAutomaticFailover |> Option.toNullable
                        enableMultipleWriteLocations = this.EnableMultipleWriteLocations |> Option.toNullable
                        locations =
                            // Locations has to be specified for the account to be gremlin enabled.
                            // Otherwise graph database provisioning fails.
                            match this.FailoverLocations, (this.Serverless = Enabled || this.Kind = Gremlin) with
                            | [], true ->
                                box [
                                    {|
                                        locationName = this.Location.ArmValue
                                    |}
                                ]
                            | [], false -> null
                            | locations, _ -> box locations
                        publicNetworkAccess = string this.PublicNetworkAccess
                        enableFreeTier = this.FreeTier
                        capabilities =
                            if this.Serverless = Enabled || this.Kind = Gremlin then
                                box [
                                    if this.Serverless = Enabled then
                                        {| name = "EnableServerless" |}
                                    if this.Kind = Gremlin then
                                        {| name = "EnableGremlin" |}
                                ]
                            else
                                null

                    |}
                    |> box
        |}