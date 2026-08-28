targetScope = 'resourceGroup'

@description('Logic App workflow name.')
param name string

@description('Azure region for the workflow and managed API connections.')
param location string

@description('Resource ID of the user-assigned managed identity used by the workflow.')
param identityResourceId string

@description('Service Bus namespace name.')
param serviceBusNamespaceName string

@description('Session-enabled Service Bus queue name.')
param serviceBusQueueName string

@description('Microsoft Teams team ID configured after deployment.')
param teamsTeamId string = ''

@description('Microsoft Teams channel ID configured after deployment.')
param teamsChannelId string = ''

@description('Semicolon-separated Outlook recipients configured after deployment.')
param notificationRecipients string = ''

@description('Log Analytics workspace resource ID for workflow diagnostics.')
param logAnalyticsWorkspaceResourceId string

@description('Tags applied to the workflow and API connections.')
param tags object = {}

var serviceBusConnectionName = take('${name}-servicebus', 80)
var teamsConnectionName = take('${name}-teams', 80)
var outlookConnectionName = take('${name}-outlook', 80)

resource serviceBusConnection 'Microsoft.Web/connections@2016-06-01' = {
  name: serviceBusConnectionName
  location: location
  tags: tags
  properties: {
    api: {
      id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'servicebus')
    }
    displayName: 'Budget Manager Service Bus'
    #disable-next-line BCP089
    parameterValueSet: {
      name: 'managedIdentityAuth'
      values: {
        namespaceEndpoint: {
          value: 'sb://${serviceBusNamespaceName}.servicebus.windows.net/'
        }
      }
    }
  }
}

resource teamsConnection 'Microsoft.Web/connections@2016-06-01' = {
  name: teamsConnectionName
  location: location
  tags: tags
  properties: {
    api: {
      id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'teams')
    }
    displayName: 'Budget Manager Teams'
  }
}

resource outlookConnection 'Microsoft.Web/connections@2016-06-01' = {
  name: outlookConnectionName
  location: location
  tags: tags
  properties: {
    api: {
      id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'office365')
    }
    displayName: 'Budget Manager Outlook'
  }
}

module workflow 'br/public:avm/res/logic/workflow:0.6.0' = {
  name: 'workflow-${uniqueString(name)}'
  params: {
    definitionParameters: {
      '$connections': {
        value: {
          outlook: {
            connectionId: outlookConnection.id
            connectionName: outlookConnection.name
            id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'office365')
          }
          servicebus: {
            connectionId: serviceBusConnection.id
            connectionName: serviceBusConnection.name
            connectionProperties: {
              authentication: {
                identity: identityResourceId
                type: 'ManagedServiceIdentity'
              }
            }
            id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'servicebus')
          }
          teams: {
            connectionId: teamsConnection.id
            connectionName: teamsConnection.name
            id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'teams')
          }
        }
      }
    }
    diagnosticSettings: [
      {
        name: 'send-to-log-analytics'
        workspaceResourceId: logAnalyticsWorkspaceResourceId
      }
    ]
    location: location
    managedIdentities: {
      userAssignedResourceIds: [
        identityResourceId
      ]
    }
    name: name
    state: 'Disabled'
    tags: tags
    workflowActions: {
      Resolve_Outlook_recipients: {
        inputs: '@join(union(coalesce(triggerBody()?[\'recipients\']?[\'userPrincipalNames\'], json(\'[]\')), coalesce(triggerBody()?[\'recipients\']?[\'ownerPrincipalNames\'], json(\'[]\')), coalesce(triggerBody()?[\'recipients\']?[\'adminPrincipalNames\'], json(\'[]\'))), \';\')'
        runAfter: {}
        type: 'Compose'
      }
      Send_a_Teams_channel_message: {
        actions: {
          Post_to_admin_channel: {
            inputs: {
              body: {
                messageBody: '<p><strong>@{coalesce(triggerBody()?[\'subject\'], \'GitHub Copilot Budget Manager event\')}</strong></p><p>@{coalesce(triggerBody()?[\'payload\']?[\'summary\'], \'Open Budget Manager for details.\')}</p>'
                recipient: {
                  channelId: teamsChannelId
                  groupId: teamsTeamId
                }
              }
              host: {
                connection: {
                  name: '@parameters(\'$connections\')[\'teams\'][\'connectionId\']'
                }
              }
              method: 'post'
              path: '/beta/teams/conversation/message/poster/@{encodeURIComponent(\'Flow bot\')}/location/@{encodeURIComponent(\'Channel\')}'
            }
            type: 'ApiConnection'
          }
        }
        else: {
          actions: {}
        }
        expression: {
          and: [
            {
              not: {
                equals: [
                  '@triggerBody()?[\'payload\']?[\'scopeKind\']'
                  'User'
                ]
              }
            }
          ]
        }
        runAfter: {}
        type: 'If'
      }
      Send_an_Outlook_email: {
        inputs: {
          body: {
            Body: '<p><strong>@{coalesce(triggerBody()?[\'subject\'], \'GitHub Copilot Budget Manager event\')}</strong></p><p>@{coalesce(triggerBody()?[\'payload\']?[\'summary\'], \'Open Budget Manager for details.\')}</p><p><a href="@{triggerBody()?[\'dashboardUrl\']}">Open Budget Manager</a></p>'
            Importance: 'Normal'
            Subject: '@{coalesce(triggerBody()?[\'subject\'], \'GitHub Copilot Budget Manager event\')}'
            To: '@{if(empty(outputs(\'Resolve_Outlook_recipients\')), \'${notificationRecipients}\', outputs(\'Resolve_Outlook_recipients\'))}'
          }
          host: {
            connection: {
              name: '@parameters(\'$connections\')[\'outlook\'][\'connectionId\']'
            }
          }
          method: 'post'
          path: '/v2/Mail'
        }
        runAfter: {
          Resolve_Outlook_recipients: [
            'Succeeded'
          ]
        }
        type: 'ApiConnection'
      }
    }
    workflowParameters: {
      '$connections': {
        defaultValue: {}
        type: 'Object'
      }
    }
    workflowTriggers: {
      When_a_workflow_event_is_available: {
        inputs: {
          host: {
            connection: {
              name: '@parameters(\'$connections\')[\'servicebus\'][\'connectionId\']'
            }
          }
          method: 'get'
          path: '/@{encodeURIComponent(encodeURIComponent(\'${serviceBusQueueName}\'))}/messages/head/peek'
          queries: {
            queueType: 'Main'
          }
        }
        recurrence: {
          frequency: 'Minute'
          interval: 1
        }
        runtimeConfiguration: {
          concurrency: {
            runs: 1
          }
        }
        type: 'ApiConnection'
      }
    }
  }
}

output workflowName string = workflow.outputs.name
output workflowResourceId string = workflow.outputs.resourceId
output connectionNames object = {
  outlook: outlookConnection.name
  serviceBus: serviceBusConnection.name
  teams: teamsConnection.name
}