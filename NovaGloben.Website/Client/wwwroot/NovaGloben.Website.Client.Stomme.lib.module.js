// Blazor's JS initializer for this app; Blazor loads it by its name, the assembly's name.
//
// An enhanced navigation or form post copies the server's <html> attributes onto the page. Unless a theme fixes the colour
// mode, the server renders no data-theme, so the visitor's light or dark choice that Stomme's App.razor script set is
// dropped, and an admin page falls back to Stomme's dark defaults until a reload (seen after Add user on /admin/users).
// Stomme's script re-applies the mode only when data-theme-fixed changes, so this re-applies it after every enhanced load.
// Remove it once Stomme does the same itself.
export function afterWebStarted(blazor) {
    blazor.addEventListener('enhancedload', () => {
        const theme = window.stommeTheme;
        if (theme && theme.apply && theme.stored) {
            theme.apply(theme.stored());
        }
    });
}
