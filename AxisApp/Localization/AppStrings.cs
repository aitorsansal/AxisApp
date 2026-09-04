namespace AxisApp.Localization;

/// <summary>
/// Plain hand-written translation tables rather than .resx/ResourceManager satellite assemblies —
/// deliberately, since this project's convention (see CLAUDE.md) is to distrust unverified SDK
/// behavior until confirmed against a real local build, and the ResX designer-generation step
/// depends on IDE/build tooling that can't be exercised here. A dictionary keyed by ISO language
/// code is simple, has no build-time code-gen dependency, and needs no linker configuration to
/// survive Android's trimmed release builds (satellite-assembly probing is a known trimming
/// footgun on mobile). If this ever needs to scale past a couple of languages, that's the moment
/// to revisit .resx — not before.
/// </summary>
public static class AppStrings
{
    public const string English = "en";
    public const string Spanish = "es";

    public static readonly IReadOnlyList<string> SupportedLanguages = [English, Spanish];

    private static readonly Dictionary<string, string> En = new()
    {
        // Common
        ["Common_Cancel"] = "Cancel",
        ["Common_Yes"] = "Yes",
        ["Common_OK"] = "OK",
        ["Common_Error"] = "Something went wrong",
        ["Common_Save"] = "Save",
        ["Common_Add"] = "Add",
        ["Common_Create"] = "Create",
        ["Common_Join"] = "Join",
        ["Common_Link"] = "Link",
        ["Common_Resend"] = "Resend",
        ["Common_Share"] = "Share",
        ["Common_SettledUp"] = "Settled up",
        ["Common_Today"] = "Today",

        // Login
        ["Login_EmailPlaceholder"] = "Email",
        ["Login_PasswordPlaceholder"] = "Password",
        ["Login_SignIn"] = "Sign In",
        ["Login_CreateAccount"] = "Create Account",
        ["Login_SignInFailed"] = "Sign in failed.",
        ["Login_SignUpFailed"] = "Sign up failed.",
        ["Login_ForgotPassword"] = "Forgot your password?",
        ["Login_ForgotPasswordNeedsEmail"] = "Enter your email above first.",
        ["Login_ForgotPasswordFailed"] = "Couldn't send the reset email.",
        ["Login_ForgotPasswordSent"] = "Check your email for a password reset link.",
        ["Login_Or"] = "OR",
        ["Login_ContinueWithGoogle"] = "Continue with Google",

        // Groups
        ["Groups_Title"] = "Groups",
        ["Groups_YourGroups"] = "Your groups",
        ["Groups_JoinWithCode"] = "Join with code",
        ["Groups_EmptyState"] = "No groups yet — tap New group to start one.",
        ["Groups_Profile"] = "Profile",
        ["Groups_LogOut"] = "Log out",
        ["Groups_MemberSingular"] = "{0} member",
        ["Groups_MemberPlural"] = "{0} members",
        ["Groups_YoureOwed"] = "you're owed",
        ["Groups_YouOwe"] = "you owe",

        // New group
        ["NewGroup_Title"] = "New group",
        ["NewGroup_GroupName"] = "Group name",
        ["NewGroup_NamePlaceholder"] = "e.g. Roommates",
        ["NewGroup_EnterName"] = "Enter a group name.",

        // Group detail
        ["GroupDetail_InvitePeople"] = "+ Invite people",
        ["GroupDetail_Balances"] = "Balances",
        ["GroupDetail_Detailed"] = "Detailed",
        ["GroupDetail_Settle"] = "Settle",
        ["GroupDetail_RecentActivity"] = "Recent activity",
        ["GroupDetail_PaidSplit"] = "{0} paid · split {1} ways",
        ["GroupDetail_Paid"] = "{0} paid {1}",
        ["GroupDetail_SettleUp"] = "Settle up",
        ["GroupDetail_SomeoneCapitalized"] = "Someone",
        ["GroupDetail_SomeoneLower"] = "someone",
        ["GroupDetail_JustNow"] = "just now",
        ["GroupDetail_MinutesAgo"] = "{0}m ago",
        ["GroupDetail_HoursAgo"] = "{0}h ago",
        ["GroupDetail_DaysAgo"] = "{0}d ago",
        ["GroupDetail_OwesYou"] = "owes you",
        ["GroupDetail_YouOwe"] = "you owe",
        ["GroupDetail_Pays"] = "pays {0}",
        ["GroupDetail_GroupOptions"] = "Group options",
        ["GroupDetail_RenameGroup"] = "Rename group",
        ["GroupDetail_ViewMembers"] = "View members",
        ["GroupDetail_LeaveGroup"] = "Leave group",
        ["GroupDetail_TransferOwnership"] = "Transfer ownership",
        ["GroupDetail_DissolveGroup"] = "Dissolve group",
        ["GroupDetail_LeaveGroupTitle"] = "Leave this group?",
        ["GroupDetail_LeaveGroupConfirm"] = "You'll lose access to this group's balances and activity. This can't be undone.",
        ["GroupDetail_DissolveGroupTitle"] = "Dissolve this group?",
        ["GroupDetail_DissolveGroupConfirm"] = "Everyone will lose this group. Past expenses and payments stay in each person's own history, but this group can't be undone.",
        ["GroupDetail_DissolveGroupConfirmWithBalances"] = "This group has unsettled balances — dissolving stops tracking them. Past expenses and payments stay in each person's own history, but this can't be undone.",
        ["GroupDetail_TransferOwnershipTitle"] = "Transfer ownership to",
        ["GroupDetail_NoTransferCandidates"] = "No one else here has an Axis account to transfer to yet.",

        // Add expense
        ["AddExpense_Title"] = "Add expense",
        ["AddExpense_EditTitle"] = "Edit expense",
        ["AddExpense_RecurringTitle"] = "Add repeating expense",
        ["AddExpense_EditRecurringTitle"] = "Edit repeating expense",
        ["AddExpense_EditSettlementTitle"] = "Edit settlement",
        ["AddExpense_DescriptionPlaceholder"] = "Description",
        ["AddExpense_PaidBy"] = "Paid by",
        ["AddExpense_Split"] = "Split",
        ["AddExpense_Equally"] = "Equally",
        ["AddExpense_Manually"] = "Manually",
        ["AddExpense_Remaining"] = "Remaining: {0:0.00}",
        ["AddExpense_Category"] = "Category",
        ["AddExpense_Repeat"] = "Repeat",
        ["AddExpense_Frequency"] = "Frequency",
        ["AddExpense_ReceiptPhoto"] = "Add receipt photo (optional)",
        ["AddExpense_TakePhoto"] = "Take photo",
        ["AddExpense_ChooseFromGallery"] = "Choose from gallery",
        ["AddExpense_ChangePhoto"] = "Change photo",
        ["AddExpense_RemovePhoto"] = "Remove photo",
        ["AddExpense_SaveExpense"] = "Save expense",
        ["AddExpense_DeleteExpense"] = "Delete expense",

        // Recurring frequency options
        ["Recurring_Frequency_Daily"] = "Daily",
        ["Recurring_Frequency_Weekly"] = "Weekly",
        ["Recurring_Frequency_Monthly"] = "Monthly",
        ["Recurring_Frequency_Yearly"] = "Yearly",

        // Repeating expenses list
        ["GroupDetail_RepeatingExpenses"] = "Repeating expenses",
        ["RecurringExpenses_SectionHeader"] = "Repeating expenses",
        ["RecurringExpenses_Add"] = "+ Add",
        ["RecurringExpenses_Empty"] = "No repeating expenses yet.",
        ["RecurringExpenses_Active"] = "Active",
        ["RecurringExpenses_Paused"] = "Paused",
        ["RecurringExpenses_Delete"] = "Delete",
        ["RecurringExpenses_DeleteConfirmTitle"] = "Delete repeating expense?",
        ["RecurringExpenses_DeleteConfirmMessage"] = "This stops future occurrences. Expenses already created from it are not affected.",

        // Categories (fixed, key-based — see AppConstants.Categories)
        ["Category_food"] = "Food",
        ["Category_transport"] = "Transport",
        ["Category_rent"] = "Rent",
        ["Category_utilities"] = "Utilities",
        ["Category_entertainment"] = "Entertainment",
        ["Category_other"] = "Other",

        // Join group
        ["JoinGroup_Title"] = "Invite people",
        ["JoinGroup_CopyLink"] = "Copy link",
        ["JoinGroup_OrJoinAGroup"] = "or join a group",
        ["JoinGroup_CodePlaceholder"] = "Enter invite code",
        ["JoinGroup_AddMemberByName"] = "Add a member by name",
        ["JoinGroup_NamePlaceholder"] = "Name (no account needed yet)",
        ["JoinGroup_AlreadyElsewhere"] = "Already added elsewhere — link instead?",
        ["JoinGroup_AlreadyOnAxis"] = "Already on Axis — send them the invite code instead",
        ["JoinGroup_PendingInvites"] = "Pending invites",
        ["JoinGroup_PhantomMember"] = "Phantom member",
        ["JoinGroup_LinkCopied"] = "Invite link copied",
        ["JoinGroup_NewLinkCopied"] = "New invite link for {0} copied",
        ["JoinGroup_Joined"] = "Joined group",
        ["JoinGroup_ShareText"] = "Join my Axis group \"{0}\": {1}",
        ["JoinGroup_ShareTitle"] = "Invite to Axis",

        // Members
        ["Members_SectionHeader"] = "Members",
        ["Members_You"] = "You",
        ["Members_Remove"] = "Remove",
        ["Members_RemoveConfirmTitle"] = "Remove {0}?",
        ["Members_RemoveConfirmMessage"] = "They'll lose access to this group. Past expenses and payments stay in their history. This can't be undone.",

        // Profile
        ["Profile_Title"] = "Profile",
        ["Profile_DisplayName"] = "Display name",
        ["Profile_DisplayNamePlaceholder"] = "Your name",
        ["Profile_Birthday"] = "Birthday",
        ["Profile_BirthdayNotSet"] = "Not set",
        ["Profile_SetBirthday"] = "Set birthday",
        ["Profile_ClearBirthday"] = "Clear",
        ["Profile_ChangePhoto"] = "Change photo",
        ["Profile_RemovePhoto"] = "Remove photo",
        ["Profile_Language"] = "Language",
        ["Profile_LanguageSystem"] = "System",
        ["Profile_LanguageEnglish"] = "English",
        ["Profile_LanguageSpanish"] = "Español",
        ["Profile_AccentColor"] = "Accent color",
        ["Profile_Email"] = "Email",
        ["Profile_NewEmailPlaceholder"] = "New email",
        ["Profile_ChangeEmail"] = "Change email",
        ["Profile_EmailUpdateSent"] = "Check your new email to confirm the change.",
        ["Profile_Password"] = "Password",
        ["Profile_NewPasswordPlaceholder"] = "New password",
        ["Profile_ChangePassword"] = "Change password",
        ["Profile_PasswordUpdated"] = "Password updated.",
        ["Profile_ProfileSaved"] = "Saved.",
        ["Profile_DeleteAccountSection"] = "Danger zone",
        ["Profile_DeleteAccountDescription"] = "Permanently delete your account and credentials. Shared expenses and payments stay in the group's history, but you'll need to transfer ownership or dissolve any group you own that still has other members first.",
        ["Profile_DeleteAccountButton"] = "Delete account",
        ["Profile_DeleteAccountTitle"] = "Delete your account?",
        ["Profile_DeleteAccountConfirm"] = "This permanently deletes your account and can't be undone. Shared history stays with the groups you're in.",
    };

    private static readonly Dictionary<string, string> Es = new()
    {
        // Common
        ["Common_Cancel"] = "Cancelar",
        ["Common_Yes"] = "Sí",
        ["Common_OK"] = "Aceptar",
        ["Common_Error"] = "Algo salió mal",
        ["Common_Save"] = "Guardar",
        ["Common_Add"] = "Añadir",
        ["Common_Create"] = "Crear",
        ["Common_Join"] = "Unirse",
        ["Common_Link"] = "Vincular",
        ["Common_Resend"] = "Reenviar",
        ["Common_Share"] = "Compartir",
        ["Common_SettledUp"] = "Todo saldado",
        ["Common_Today"] = "Hoy",

        // Login
        ["Login_EmailPlaceholder"] = "Correo electrónico",
        ["Login_PasswordPlaceholder"] = "Contraseña",
        ["Login_SignIn"] = "Iniciar sesión",
        ["Login_CreateAccount"] = "Crear cuenta",
        ["Login_SignInFailed"] = "No se pudo iniciar sesión.",
        ["Login_SignUpFailed"] = "No se pudo crear la cuenta.",
        ["Login_ForgotPassword"] = "¿Olvidaste tu contraseña?",
        ["Login_ForgotPasswordNeedsEmail"] = "Introduce tu correo electrónico primero.",
        ["Login_ForgotPasswordFailed"] = "No se pudo enviar el correo de recuperación.",
        ["Login_ForgotPasswordSent"] = "Revisa tu correo para restablecer la contraseña.",
        ["Login_Or"] = "O",
        ["Login_ContinueWithGoogle"] = "Continuar con Google",

        // Groups
        ["Groups_Title"] = "Grupos",
        ["Groups_YourGroups"] = "Tus grupos",
        ["Groups_JoinWithCode"] = "Unirse con código",
        ["Groups_EmptyState"] = "Aún no tienes grupos — toca Nuevo grupo para crear uno.",
        ["Groups_Profile"] = "Perfil",
        ["Groups_LogOut"] = "Cerrar sesión",
        ["Groups_MemberSingular"] = "{0} miembro",
        ["Groups_MemberPlural"] = "{0} miembros",
        ["Groups_YoureOwed"] = "te deben",
        ["Groups_YouOwe"] = "debes",

        // New group
        ["NewGroup_Title"] = "Nuevo grupo",
        ["NewGroup_GroupName"] = "Nombre del grupo",
        ["NewGroup_NamePlaceholder"] = "p. ej. Compañeros de piso",
        ["NewGroup_EnterName"] = "Introduce un nombre de grupo.",

        // Group detail
        ["GroupDetail_InvitePeople"] = "+ Invitar personas",
        ["GroupDetail_Balances"] = "Saldos",
        ["GroupDetail_Detailed"] = "Detallado",
        ["GroupDetail_Settle"] = "Saldar",
        ["GroupDetail_RecentActivity"] = "Actividad reciente",
        ["GroupDetail_PaidSplit"] = "{0} pagó · dividido entre {1}",
        ["GroupDetail_Paid"] = "{0} pagó a {1}",
        ["GroupDetail_SettleUp"] = "Saldar cuenta",
        ["GroupDetail_SomeoneCapitalized"] = "Alguien",
        ["GroupDetail_SomeoneLower"] = "alguien",
        ["GroupDetail_JustNow"] = "ahora mismo",
        ["GroupDetail_MinutesAgo"] = "hace {0}m",
        ["GroupDetail_HoursAgo"] = "hace {0}h",
        ["GroupDetail_DaysAgo"] = "hace {0}d",
        ["GroupDetail_OwesYou"] = "te debe",
        ["GroupDetail_YouOwe"] = "le debes",
        ["GroupDetail_Pays"] = "paga a {0}",
        ["GroupDetail_GroupOptions"] = "Opciones del grupo",
        ["GroupDetail_RenameGroup"] = "Renombrar grupo",
        ["GroupDetail_ViewMembers"] = "Ver miembros",
        ["GroupDetail_LeaveGroup"] = "Salir del grupo",
        ["GroupDetail_TransferOwnership"] = "Transferir propiedad",
        ["GroupDetail_DissolveGroup"] = "Disolver grupo",
        ["GroupDetail_LeaveGroupTitle"] = "¿Salir de este grupo?",
        ["GroupDetail_LeaveGroupConfirm"] = "Perderás el acceso a los saldos y la actividad de este grupo. Esto no se puede deshacer.",
        ["GroupDetail_DissolveGroupTitle"] = "¿Disolver este grupo?",
        ["GroupDetail_DissolveGroupConfirm"] = "Todos perderán este grupo. Los gastos y pagos pasados se conservan en el historial de cada persona, pero esto no se puede deshacer.",
        ["GroupDetail_DissolveGroupConfirmWithBalances"] = "Este grupo tiene saldos sin liquidar — al disolverlo dejarán de rastrearse. Los gastos y pagos pasados se conservan en el historial de cada persona, pero esto no se puede deshacer.",
        ["GroupDetail_TransferOwnershipTitle"] = "Transferir propiedad a",
        ["GroupDetail_NoTransferCandidates"] = "Nadie más aquí tiene cuenta en Axis todavía para transferir la propiedad.",
        ["GroupDetail_RepeatingExpenses"] = "Gastos recurrentes",

        // Add expense
        ["AddExpense_Title"] = "Añadir gasto",
        ["AddExpense_EditTitle"] = "Editar gasto",
        ["AddExpense_RecurringTitle"] = "Añadir gasto recurrente",
        ["AddExpense_EditRecurringTitle"] = "Editar gasto recurrente",
        ["AddExpense_EditSettlementTitle"] = "Editar liquidación",
        ["AddExpense_DescriptionPlaceholder"] = "Descripción",
        ["AddExpense_PaidBy"] = "Pagado por",
        ["AddExpense_Split"] = "Reparto",
        ["AddExpense_Equally"] = "A partes iguales",
        ["AddExpense_Manually"] = "Manual",
        ["AddExpense_Remaining"] = "Restante: {0:0.00}",
        ["AddExpense_Category"] = "Categoría",
        ["AddExpense_Repeat"] = "Repetir",
        ["AddExpense_Frequency"] = "Frecuencia",
        ["AddExpense_ReceiptPhoto"] = "Añadir foto del recibo (opcional)",
        ["AddExpense_TakePhoto"] = "Hacer foto",
        ["AddExpense_ChooseFromGallery"] = "Elegir de la galería",
        ["AddExpense_ChangePhoto"] = "Cambiar foto",
        ["AddExpense_RemovePhoto"] = "Quitar foto",
        ["AddExpense_SaveExpense"] = "Guardar gasto",
        ["AddExpense_DeleteExpense"] = "Eliminar gasto",

        // Recurring frequency options
        ["Recurring_Frequency_Daily"] = "Diaria",
        ["Recurring_Frequency_Weekly"] = "Semanal",
        ["Recurring_Frequency_Monthly"] = "Mensual",
        ["Recurring_Frequency_Yearly"] = "Anual",

        // Repeating expenses list
        ["RecurringExpenses_SectionHeader"] = "Gastos recurrentes",
        ["RecurringExpenses_Add"] = "+ Añadir",
        ["RecurringExpenses_Empty"] = "Todavía no hay gastos recurrentes.",
        ["RecurringExpenses_Active"] = "Activo",
        ["RecurringExpenses_Paused"] = "Pausado",
        ["RecurringExpenses_Delete"] = "Eliminar",
        ["RecurringExpenses_DeleteConfirmTitle"] = "¿Eliminar el gasto recurrente?",
        ["RecurringExpenses_DeleteConfirmMessage"] = "Esto detiene futuras repeticiones. Los gastos ya creados a partir de él no se ven afectados.",

        // Categories
        ["Category_food"] = "Comida",
        ["Category_transport"] = "Transporte",
        ["Category_rent"] = "Alquiler",
        ["Category_utilities"] = "Suministros",
        ["Category_entertainment"] = "Ocio",
        ["Category_other"] = "Otros",

        // Join group
        ["JoinGroup_Title"] = "Invitar personas",
        ["JoinGroup_CopyLink"] = "Copiar enlace",
        ["JoinGroup_OrJoinAGroup"] = "o únete a un grupo",
        ["JoinGroup_CodePlaceholder"] = "Introduce el código de invitación",
        ["JoinGroup_AddMemberByName"] = "Añadir un miembro por nombre",
        ["JoinGroup_NamePlaceholder"] = "Nombre (sin cuenta todavía)",
        ["JoinGroup_AlreadyElsewhere"] = "Ya añadido en otro sitio — ¿vincular en su lugar?",
        ["JoinGroup_AlreadyOnAxis"] = "Ya tiene cuenta en Axis — envíale el código de invitación",
        ["JoinGroup_PendingInvites"] = "Invitaciones pendientes",
        ["JoinGroup_PhantomMember"] = "Miembro sin cuenta",
        ["JoinGroup_LinkCopied"] = "Enlace de invitación copiado",
        ["JoinGroup_NewLinkCopied"] = "Nuevo enlace de invitación para {0} copiado",
        ["JoinGroup_Joined"] = "Te has unido al grupo",
        ["JoinGroup_ShareText"] = "Únete a mi grupo de Axis \"{0}\": {1}",
        ["JoinGroup_ShareTitle"] = "Invitación a Axis",

        // Members
        ["Members_SectionHeader"] = "Miembros",
        ["Members_You"] = "Tú",
        ["Members_Remove"] = "Eliminar",
        ["Members_RemoveConfirmTitle"] = "¿Eliminar a {0}?",
        ["Members_RemoveConfirmMessage"] = "Perderá el acceso a este grupo. Los gastos y pagos pasados se conservan en su historial. Esto no se puede deshacer.",

        // Profile
        ["Profile_Title"] = "Perfil",
        ["Profile_DisplayName"] = "Nombre visible",
        ["Profile_DisplayNamePlaceholder"] = "Tu nombre",
        ["Profile_Birthday"] = "Fecha de nacimiento",
        ["Profile_BirthdayNotSet"] = "Sin definir",
        ["Profile_SetBirthday"] = "Añadir fecha de nacimiento",
        ["Profile_ClearBirthday"] = "Quitar",
        ["Profile_ChangePhoto"] = "Cambiar foto",
        ["Profile_RemovePhoto"] = "Eliminar foto",
        ["Profile_Language"] = "Idioma",
        ["Profile_LanguageSystem"] = "Sistema",
        ["Profile_LanguageEnglish"] = "English",
        ["Profile_LanguageSpanish"] = "Español",
        ["Profile_AccentColor"] = "Color de acento",
        ["Profile_Email"] = "Correo electrónico",
        ["Profile_NewEmailPlaceholder"] = "Nuevo correo electrónico",
        ["Profile_ChangeEmail"] = "Cambiar correo",
        ["Profile_EmailUpdateSent"] = "Revisa tu nuevo correo para confirmar el cambio.",
        ["Profile_Password"] = "Contraseña",
        ["Profile_NewPasswordPlaceholder"] = "Nueva contraseña",
        ["Profile_ChangePassword"] = "Cambiar contraseña",
        ["Profile_PasswordUpdated"] = "Contraseña actualizada.",
        ["Profile_ProfileSaved"] = "Guardado.",
        ["Profile_DeleteAccountSection"] = "Zona de peligro",
        ["Profile_DeleteAccountDescription"] = "Elimina tu cuenta y credenciales permanentemente. Los gastos y pagos compartidos se conservan en el historial del grupo, pero antes deberás transferir la propiedad o disolver cualquier grupo tuyo que aún tenga otros miembros.",
        ["Profile_DeleteAccountButton"] = "Eliminar cuenta",
        ["Profile_DeleteAccountTitle"] = "¿Eliminar tu cuenta?",
        ["Profile_DeleteAccountConfirm"] = "Esto elimina tu cuenta de forma permanente y no se puede deshacer. El historial compartido permanece en los grupos en los que participas.",
    };

    private static readonly Dictionary<string, Dictionary<string, string>> Tables = new()
    {
        [English] = En,
        [Spanish] = Es,
    };

    /// <summary>Looks up <paramref name="key"/> in <paramref name="languageCode"/>'s table, falling
    /// back to English, then to the key itself (visibly wrong rather than silently blank, so a
    /// missing translation is obvious instead of hidden) if neither has it.</summary>
    public static string Get(string key, string languageCode)
    {
        if (Tables.TryGetValue(languageCode, out var table) && table.TryGetValue(key, out var value))
            return value;

        return En.TryGetValue(key, out var fallback) ? fallback : key;
    }
}
