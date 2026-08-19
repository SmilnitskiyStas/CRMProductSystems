import { fireEvent, render } from '@testing-library/react-native';
import { StatusCard } from '../StatusCard';

describe('<StatusCard />', () => {
  test('renders its label/count and invokes the action', async () => {
    const onPress = jest.fn();
    const screen = await render(
      <StatusCard
        label="Critical"
        count={7}
        colorClass="text-red-700"
        bgClass="bg-red-50"
        onPress={onPress}
      />
    );

    expect(screen.getByText('Critical')).toBeOnTheScreen();
    expect(screen.getByText('7')).toBeOnTheScreen();
    fireEvent.press(screen.getByText('Critical'));
    expect(onPress).toHaveBeenCalledTimes(1);
  });
});
