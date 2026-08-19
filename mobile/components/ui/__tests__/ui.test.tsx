import { fireEvent, render } from '@testing-library/react-native';
import { Button, EmptyState, OfflineBanner, SelectField, TextField } from '../index';

describe('mobile UI foundation', () => {
  test('Button exposes its accessible action', async () => {
    const onPress = jest.fn();
    const screen = await render(<Button label="Зберегти" onPress={onPress} />);
    fireEvent.press(screen.getByRole('button', { name: 'Зберегти' }));
    expect(onPress).toHaveBeenCalledTimes(1);
  });

  test('TextField associates its label and announces validation feedback', async () => {
    const screen = await render(<TextField label="Email" value="" error="Обов’язкове поле" />);
    expect(screen.getByLabelText('Email')).toBeOnTheScreen();
    expect(screen.getByText('Обов’язкове поле')).toBeOnTheScreen();
  });

  test('SelectField selects an option without a platform-only dependency', async () => {
    const onChange = jest.fn();
    const screen = await render(<SelectField label="Магазин" options={[{ label: 'Центр', value: 'center' }]} onChange={onChange} />);
    await fireEvent.press(screen.getByRole('button', { name: 'Магазин' }));
    fireEvent.press(screen.getByText('Центр'));
    expect(onChange).toHaveBeenCalledWith('center');
  });

  test('shared states render actionable and alert semantics', async () => {
    const onAction = jest.fn();
    const screen = await render(<><EmptyState title="Порожньо" actionLabel="Додати" onAction={onAction} /><OfflineBanner visible /></>);
    fireEvent.press(screen.getByRole('button', { name: 'Додати' }));
    expect(onAction).toHaveBeenCalledTimes(1);
    expect(screen.getByText('Немає з’єднання. Зміни буде збережено локально.')).toBeOnTheScreen();
  });
});
