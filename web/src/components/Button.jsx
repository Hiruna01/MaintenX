/**
 * Shared button. `variant` picks the CSS class; everything else (type, onClick, disabled,
 * aria-*) is forwarded straight through to the real <button>.
 */
export function Button({ variant = 'primary', type = 'button', className = '', children, ...rest }) {
  return (
    <button type={type} className={`button button--${variant} ${className}`.trim()} {...rest}>
      {children}
    </button>
  );
}

export default Button;
