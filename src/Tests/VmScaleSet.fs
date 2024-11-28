module VmScaleSet

open Expecto
open Farmer
open Farmer.Builders
open Farmer.Vm
open Farmer.VmScaleSet
open Newtonsoft.Json.Linq

let tests =
    testList "Virtual Machine Scale Set" [
        test "Create a basic scale set with autogenerated vnet" {
            let deployment = arm {
                add_resources [
                    vmss {
                        name "my-scale-set"
                        capacity 3

                        vm_profile (
                            vm {
                                username "azureuser"
                                operating_system UbuntuServer_2204LTS
                                vm_size Standard_B1s
                                os_disk 128 StandardSSD_LRS
                                diagnostics_support
                            }
                        )
                    }
                ]
            }

            let jobj = deployment.Template |> Writer.toJson |> JToken.Parse
            let vnet = jobj.SelectToken("resources[?(@.name=='my-scale-set-vnet')]")
            Expect.isNotNull vnet "Vnet resource not generated"
            let vmss = jobj.SelectToken("resources[?(@.name=='my-scale-set')]")
            Expect.isNotNull vmss "Scale set resource not generated"

            Expect.contains
                vmss["dependsOn"]
                (JValue "[resourceId('Microsoft.Network/virtualNetworks', 'my-scale-set-vnet')]")
                "VMSS is missing dependency on vnet"

            Expect.isNotNull
                (jobj.SelectToken("parameters.password-for-my-scale-set"))
                "VMSS did not generate password for VM instance"

            Expect.equal (vmss.SelectToken("sku.capacity").ToString()) "3" "Incorrect capacity on VMSS"
            let vmssProps = vmss["properties"]
            Expect.isNotNull vmssProps "VMSS is missing 'properties'"

            Expect.equal
                (vmssProps.SelectToken("scaleInPolicy.rules[0]").ToString())
                "Default"
                "Incorrect scale in policy on VMSS"

            Expect.equal
                (vmssProps.SelectToken("upgradePolicy.mode").ToString())
                "Automatic"
                "Incorrect upgrade policy on VMSS"

            let vmProfile = vmssProps.SelectToken("virtualMachineProfile")
            Expect.isNotNull vmProfile "VMSS is missing VM profile"

            Expect.hasLength
                (vmProfile.SelectToken("networkProfile.networkInterfaceConfigurations"))
                1
                "Incorrect number of NIC configs on VMSS"

            Expect.hasLength
                (vmProfile.SelectToken("networkProfile.networkInterfaceConfigurations[*].properties.ipConfigurations"))
                1
                "Incorrect number of IP configs on VMSS"

            Expect.equal
                (vmProfile
                    .SelectToken(
                        "networkProfile.networkInterfaceConfigurations[*].properties.ipConfigurations[0].properties.subnet.id"
                    )
                    .ToString())
                "[resourceId('Microsoft.Network/virtualNetworks/subnets', 'my-scale-set-vnet', 'my-scale-set-subnet')]"
                "VMSS IP config has incorrect subnet ID"

            Expect.equal
                (vmProfile.SelectToken("osProfile.adminPassword").ToString())
                "[parameters('password-for-my-scale-set')]"
                "VMSS OS profile does not get password from parameters"

            Expect.equal
                (vmProfile.SelectToken("osProfile.computerNamePrefix").ToString())
                "my-scale-set"
                "VMSS OS profile has incorrect computer name prefix"
        }
        test "Create a basic scale set using a gallery image" {
            let deployment = arm {
                add_resources [
                    vmss {
                        name "my-scale-set"

                        vm_profile (
                            vm {
                                username "azureuser"
                                operating_system (Linux, SharedGalleryImageId "test-image-id")
                                vm_size Standard_B1s
                                os_disk 128 StandardSSD_LRS
                            }
                        )
                    }
                ]
            }

            let jobj = deployment.Template |> Writer.toJson |> JToken.Parse
            let vmss = jobj.SelectToken("resources[?(@.name=='my-scale-set')]")
            Expect.isNotNull vmss "Scale set resource not generated"
            let vmssProps = vmss["properties"]
            Expect.isNotNull vmssProps "VMSS is missing 'properties'"
            let vmProfile = vmssProps.SelectToken("virtualMachineProfile")
            Expect.isNotNull vmProfile "VMSS is missing VM profile"

            Expect.equal
                (vmProfile
                    .SelectToken("storageProfile.imageReference.sharedGalleryImageId")
                    .ToString())
                "test-image-id"
                "VMSS OS profile has incorrect image reference"
        }
        test "Create a scale with OS upgrade options" {
            let deployment = arm {
                add_resources [
                    vmss {
                        name "my-scale-set"

                        vm_profile (
                            vm {
                                username "azureuser"
                                operating_system (Linux, SharedGalleryImageId "test-image-id")
                                vm_size Standard_B1s
                                os_disk 128 StandardSSD_LRS
                            }
                        )

                        osupgrade_automatic true
                        osupgrade_rolling_upgrade true
                    }
                ]
            }

            let jobj = deployment.Template |> Writer.toJson |> JToken.Parse
            let vmss = jobj.SelectToken("resources[?(@.name=='my-scale-set')]")
            Expect.isNotNull vmss "Scale set resource not generated"
            let vmssProps = vmss["properties"]
            Expect.isNotNull vmssProps "VMSS is missing 'properties'"

            Expect.equal
                (vmssProps
                    .SelectToken("upgradePolicy.automaticOSUpgradePolicy.enableAutomaticOSUpgrade")
                    .ToString())
                (string true)
                "VMSS OS upgrade policy is expected to be enabled"

            Expect.equal
                (vmssProps
                    .SelectToken("upgradePolicy.automaticOSUpgradePolicy.useRollingUpgradePolicy")
                    .ToString())
                (string true)
                "VMSS OS rolling upgrade policy is expected to be enabled"

            Expect.isNull
                (vmssProps.SelectToken("upgradePolicy.automaticOSUpgradePolicy.disableAutomaticRollback"))
                "VMSS OS automatic upgrade rollback is not expected to be set"
        }
        test "Create a scale set linking to existing vnet" {
            let deployment = arm {
                add_resources [
                    vnet {
                        name "my-net"
                        add_address_spaces [ "10.100.200.0/24" ]

                        add_subnets [
                            subnet {
                                name "scale-set-subnet"
                                prefix "10.100.200.0/28"
                            }
                        ]
                    }
                    vmss {
                        name "my-scale-set"
                        capacity 3

                        vm_profile (
                            vm {
                                username "azureuser"
                                operating_system UbuntuServer_2204LTS
                                vm_size Standard_B1s
                                os_disk 128 StandardSSD_LRS
                                diagnostics_support
                                link_to_vnet "my-net"
                                subnet_name "scale-set-subnet"
                            }
                        )

                        scale_in_policy OldestVM
                        scale_in_force_deletion Enabled
                        upgrade_mode Rolling
                        automatic_repair_enabled_after (System.TimeSpan.FromMinutes 10)

                        add_extensions [
                            applicationHealthExtension {
                                protocol (ApplicationHealthExtensionProtocol.HTTP "/healthcheck")
                                port 80
                                os Linux
                            }
                        ]
                    }
                ]
            }

            let jobj = deployment.Template |> Writer.toJson |> JToken.Parse
            let vnet = jobj.SelectToken("resources[?(@.name=='my-net')]")
            Expect.isNotNull vnet "Vnet resource not generated"
            let vmss = jobj.SelectToken("resources[?(@.name=='my-scale-set')]")
            Expect.isNotNull vmss "Scale set resource not generated"

            Expect.contains
                vmss["dependsOn"]
                (JValue "[resourceId('Microsoft.Network/virtualNetworks', 'my-net')]")
                "VMSS is missing dependency on vnet"

            let vmssProps = vmss["properties"]
            Expect.isNotNull vmssProps "VMSS is missing 'properties'"

            Expect.equal
                (vmssProps.SelectToken("scaleInPolicy.rules[0]").ToString())
                "OldestVM"
                "Incorrect scale in policy on VMSS"

            Expect.equal
                (vmssProps.SelectToken("upgradePolicy.mode").ToString())
                "Rolling"
                "Incorrect upgrade policy on VMSS"

            Expect.equal
                (vmssProps.SelectToken("automaticRepairsPolicy.enabled").ToString())
                "True"
                "Incorrect automatic repairs policy on VMSS"

            let vmProfile = vmssProps.SelectToken("virtualMachineProfile")
            Expect.isNotNull vmProfile "VMSS is missing VM profile"

            Expect.hasLength
                (vmProfile.SelectToken("networkProfile.networkInterfaceConfigurations"))
                1
                "Incorrect number of NIC configs on VMSS"

            Expect.hasLength
                (vmProfile.SelectToken("networkProfile.networkInterfaceConfigurations[*].properties.ipConfigurations"))
                1
                "Incorrect number of IP configs on VMSS"

            Expect.equal
                (vmProfile
                    .SelectToken(
                        "networkProfile.networkInterfaceConfigurations[*].properties.ipConfigurations[0].properties.subnet.id"
                    )
                    .ToString())
                "[resourceId('Microsoft.Network/virtualNetworks/subnets', 'my-net', 'scale-set-subnet')]"
                "VMSS IP config has incorrect subnet ID"

            Expect.equal
                (vmProfile.SelectToken("osProfile.computerNamePrefix").ToString())
                "my-scale-set"
                "VMSS OS profile has incorrect computer name prefix"
        }
    ]