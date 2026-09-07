// Plain hand-written translation tables, same convention as
// AxisApp/Localization/AppStrings.cs — a dictionary keyed by ISO language
// code, no build-time codegen or i18n library dependency. Key names mirror
// AppStrings.cs 1:1 wherever the same concept exists in both clients
// (Common_*, Category_*, Profile_*, GroupDetail_*, etc.) so the two
// dictionaries stay easy to diff against each other; web-only screens (the
// redeem-only JoinGroupPage, equal-split-only AddExpensePage) get their own
// keys where the UI genuinely differs.

export type Language = 'en' | 'es'
export const SUPPORTED_LANGUAGES: readonly Language[] = ['en', 'es']

type Dictionary = Record<string, string>

const en: Dictionary = {
  // Common
  Common_Cancel: 'Cancel',
  Common_OK: 'OK',
  Common_Error: 'Something went wrong',
  Common_Save: 'Save',
  Common_Saving: 'Saving…',
  Common_Creating: 'Creating…',
  Common_Loading: 'Loading…',
  Common_Back: 'Back',
  Common_PleaseWait: 'Please wait…',
  Common_SettledUp: 'Settled up',

  // Categories
  Category_food: 'Food',
  Category_transport: 'Transport',
  Category_rent: 'Rent',
  Category_utilities: 'Utilities',
  Category_entertainment: 'Entertainment',
  Category_other: 'Other',

  // Login
  Login_SigninSubtitle: 'Sign in to your account',
  Login_SignupSubtitle: 'Create an account',
  Login_ContinueWithGoogle: 'Continue with Google',
  Login_Or: 'or',
  Login_Email: 'Email',
  Login_Password: 'Password',
  Login_SignIn: 'Sign in',
  Login_SignUp: 'Sign up',
  Login_ToggleToSignup: "Don't have an account? Sign up",
  Login_ToggleToSignin: 'Already have an account? Sign in',

  // App header
  Groups_Profile: 'Profile',
  Groups_LogOut: 'Log out',

  // Groups
  Groups_YourGroups: 'Your groups',
  Groups_NewGroup: '+ New group',
  Groups_JoinWithCode: 'Join with code',
  Groups_EmptyState: 'No groups yet — create one or join with an invite code.',
  Groups_YoureOwedAmount: "you're owed {0} {1}",
  Groups_YouOweAmount: 'you owe {0} {1}',

  // New group
  NewGroup_Title: 'New group',
  NewGroup_GroupName: 'Group name',
  NewGroup_Currency: 'Currency',
  NewGroup_CreateButton: 'Create group',

  // Join group
  JoinGroup_JoinTitle: 'Join a group',
  JoinGroup_CodeOrLinkLabel: 'Invite code or link',
  JoinGroup_CodePlaceholder: 'Paste the code or the full invite link',
  JoinGroup_JoinButton: 'Join group',
  JoinGroup_Joining: 'Joining…',

  // Group detail
  GroupDetail_GenericTitle: 'Group',
  GroupDetail_AddExpenseButton: '+ Add expense',
  GroupDetail_ExpenseFallback: 'Expense',
  GroupDetail_Balances: 'Balances',
  GroupDetail_BalancesEmpty: "Everyone's settled up.",
  GroupDetail_OwesYouAmount: 'owes you {0} {1}',
  GroupDetail_YouOweAmount: 'you owe {0} {1}',
  GroupDetail_Settle: 'Settle',
  GroupDetail_Settling: 'Settling…',
  GroupDetail_RecentActivity: 'Recent activity',
  GroupDetail_ActivityEmpty: 'No expenses yet.',
  GroupDetail_SettleUp: 'Settle up',
  GroupDetail_SomeoneCapitalized: 'Someone',

  // Add expense
  AddExpense_Title: 'Add expense',
  AddExpense_DescriptionLabel: 'Description',
  AddExpense_DescriptionPlaceholder: 'Dinner, groceries…',
  AddExpense_AmountLabel: 'Amount ({0})',
  AddExpense_Category: 'Category',
  AddExpense_DateLabel: 'Date',
  AddExpense_PaidBy: 'Paid by',
  AddExpense_SplitEquallyBetween: 'Split equally between',
  AddExpense_SaveExpense: 'Save expense',
  AddExpense_InvalidAmount: 'Enter a valid amount',
  AddExpense_PickParticipant: 'Pick at least one participant',
  AddExpense_PickPayer: 'Pick who paid',

  // Profile
  Profile_Title: 'Profile',
  Profile_DisplayName: 'Display name',
  Profile_DisplayNamePlaceholder: 'Your name',
  Profile_Birthday: 'Birthday',
  Profile_ChangePhoto: 'Change photo',
  Profile_RemovePhoto: 'Remove photo',
  Profile_Language: 'Language',
  Profile_LanguageSystem: 'System',
  Profile_LanguageEnglish: 'English',
  Profile_LanguageSpanish: 'Español',
  Profile_Email: 'Email',
  Profile_NewEmailPlaceholder: 'New email',
  Profile_ChangeEmail: 'Change email',
  Profile_EmailUpdateSent: 'Check your new email to confirm the change.',
  Profile_Password: 'Password',
  Profile_NewPasswordPlaceholder: 'New password',
  Profile_ChangePassword: 'Change password',
  Profile_PasswordUpdated: 'Password updated.',
  Profile_ProfileSaved: 'Saved.',
  Profile_DeleteAccountSection: 'Danger zone',
  Profile_DeleteAccountDescription:
    "Permanently delete your account and credentials. Shared expenses and payments stay in the group's history, but you'll need to transfer ownership or dissolve any group you own that still has other members first.",
  Profile_DeleteAccountButton: 'Delete account',
  Profile_DeleteAccountConfirm:
    "This permanently deletes your account and can't be undone. Shared history stays with the groups you're in.",
}

const es: Dictionary = {
  // Common
  Common_Cancel: 'Cancelar',
  Common_OK: 'Aceptar',
  Common_Error: 'Algo salió mal',
  Common_Save: 'Guardar',
  Common_Saving: 'Guardando…',
  Common_Creating: 'Creando…',
  Common_Loading: 'Cargando…',
  Common_Back: 'Atrás',
  Common_PleaseWait: 'Espera…',
  Common_SettledUp: 'Todo saldado',

  // Categories
  Category_food: 'Comida',
  Category_transport: 'Transporte',
  Category_rent: 'Alquiler',
  Category_utilities: 'Suministros',
  Category_entertainment: 'Ocio',
  Category_other: 'Otros',

  // Login
  Login_SigninSubtitle: 'Inicia sesión en tu cuenta',
  Login_SignupSubtitle: 'Crea una cuenta',
  Login_ContinueWithGoogle: 'Continuar con Google',
  Login_Or: 'o',
  Login_Email: 'Correo electrónico',
  Login_Password: 'Contraseña',
  Login_SignIn: 'Iniciar sesión',
  Login_SignUp: 'Crear cuenta',
  Login_ToggleToSignup: '¿No tienes cuenta? Regístrate',
  Login_ToggleToSignin: '¿Ya tienes cuenta? Inicia sesión',

  // App header
  Groups_Profile: 'Perfil',
  Groups_LogOut: 'Cerrar sesión',

  // Groups
  Groups_YourGroups: 'Tus grupos',
  Groups_NewGroup: '+ Nuevo grupo',
  Groups_JoinWithCode: 'Unirse con código',
  Groups_EmptyState: 'Aún no tienes grupos — crea uno o únete con un código de invitación.',
  Groups_YoureOwedAmount: 'te deben {0} {1}',
  Groups_YouOweAmount: 'debes {0} {1}',

  // New group
  NewGroup_Title: 'Nuevo grupo',
  NewGroup_GroupName: 'Nombre del grupo',
  NewGroup_Currency: 'Moneda',
  NewGroup_CreateButton: 'Crear grupo',

  // Join group
  JoinGroup_JoinTitle: 'Unirse a un grupo',
  JoinGroup_CodeOrLinkLabel: 'Código o enlace de invitación',
  JoinGroup_CodePlaceholder: 'Pega el código o el enlace de invitación completo',
  JoinGroup_JoinButton: 'Unirse al grupo',
  JoinGroup_Joining: 'Uniéndose…',

  // Group detail
  GroupDetail_GenericTitle: 'Grupo',
  GroupDetail_AddExpenseButton: '+ Añadir gasto',
  GroupDetail_ExpenseFallback: 'Gasto',
  GroupDetail_Balances: 'Saldos',
  GroupDetail_BalancesEmpty: 'Todos están al día.',
  GroupDetail_OwesYouAmount: 'te debe {0} {1}',
  GroupDetail_YouOweAmount: 'le debes {0} {1}',
  GroupDetail_Settle: 'Saldar',
  GroupDetail_Settling: 'Saldando…',
  GroupDetail_RecentActivity: 'Actividad reciente',
  GroupDetail_ActivityEmpty: 'Todavía no hay gastos.',
  GroupDetail_SettleUp: 'Saldar cuenta',
  GroupDetail_SomeoneCapitalized: 'Alguien',

  // Add expense
  AddExpense_Title: 'Añadir gasto',
  AddExpense_DescriptionLabel: 'Descripción',
  AddExpense_DescriptionPlaceholder: 'Cena, compra…',
  AddExpense_AmountLabel: 'Importe ({0})',
  AddExpense_Category: 'Categoría',
  AddExpense_DateLabel: 'Fecha',
  AddExpense_PaidBy: 'Pagado por',
  AddExpense_SplitEquallyBetween: 'Dividir a partes iguales entre',
  AddExpense_SaveExpense: 'Guardar gasto',
  AddExpense_InvalidAmount: 'Introduce un importe válido',
  AddExpense_PickParticipant: 'Elige al menos un participante',
  AddExpense_PickPayer: 'Elige quién pagó',

  // Profile
  Profile_Title: 'Perfil',
  Profile_DisplayName: 'Nombre visible',
  Profile_DisplayNamePlaceholder: 'Tu nombre',
  Profile_Birthday: 'Fecha de nacimiento',
  Profile_ChangePhoto: 'Cambiar foto',
  Profile_RemovePhoto: 'Eliminar foto',
  Profile_Language: 'Idioma',
  Profile_LanguageSystem: 'Sistema',
  Profile_LanguageEnglish: 'English',
  Profile_LanguageSpanish: 'Español',
  Profile_Email: 'Correo electrónico',
  Profile_NewEmailPlaceholder: 'Nuevo correo electrónico',
  Profile_ChangeEmail: 'Cambiar correo',
  Profile_EmailUpdateSent: 'Revisa tu nuevo correo para confirmar el cambio.',
  Profile_Password: 'Contraseña',
  Profile_NewPasswordPlaceholder: 'Nueva contraseña',
  Profile_ChangePassword: 'Cambiar contraseña',
  Profile_PasswordUpdated: 'Contraseña actualizada.',
  Profile_ProfileSaved: 'Guardado.',
  Profile_DeleteAccountSection: 'Zona de peligro',
  Profile_DeleteAccountDescription:
    'Elimina tu cuenta y credenciales permanentemente. Los gastos y pagos compartidos se conservan en el historial del grupo, pero antes deberás transferir la propiedad o disolver cualquier grupo tuyo que aún tenga otros miembros.',
  Profile_DeleteAccountButton: 'Eliminar cuenta',
  Profile_DeleteAccountConfirm:
    'Esto elimina tu cuenta de forma permanente y no se puede deshacer. El historial compartido permanece en los grupos en los que participas.',
}

export const strings: Record<Language, Dictionary> = { en, es }
