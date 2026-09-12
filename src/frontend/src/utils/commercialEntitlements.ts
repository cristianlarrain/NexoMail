export const commercialEntitlements = {
  unifiedMail: 'unified_mail',
  mailActions: 'mail_actions',
  controlCenterBasic: 'control_center_basic',
  trackingBasic: 'tracking_basic',
  nexiAi: 'nexi_ai',
  controlCenterFull: 'control_center_full',
  advancedAnalytics: 'advanced_analytics',
  signaturesTemplates: 'signatures_templates',
  userManagement: 'user_management',
  rolesPolicies: 'roles_policies',
  organizationAnalytics: 'organization_analytics',
  prioritySupport: 'priority_support',
  whiteLabelBranding: 'white_label_branding',
  customDomain: 'custom_domain',
  customLimits: 'custom_limits',
} as const

export type CommercialEntitlement = typeof commercialEntitlements[keyof typeof commercialEntitlements]
