"use client";

interface StarRatingProps {
  value: number; // 0–5, supports decimals
  size?: number;
  interactive?: false;
}

interface InteractiveStarRatingProps {
  value: number;
  size?: number;
  interactive: true;
  onChange: (rating: number) => void;
}

type Props = StarRatingProps | InteractiveStarRatingProps;

export function StarRating(props: Props) {
  const { value, size = 14 } = props;
  const interactive = "interactive" in props && props.interactive;

  return (
    <span style={{ display: "inline-flex", gap: 1 }}>
      {[1, 2, 3, 4, 5].map((star) => {
        const filled = value >= star;
        const half = !filled && value >= star - 0.5;
        return (
          <svg
            key={star}
            width={size}
            height={size}
            viewBox="0 0 16 16"
            fill="none"
            xmlns="http://www.w3.org/2000/svg"
            style={{ cursor: interactive ? "pointer" : "default", flexShrink: 0 }}
            onClick={() => {
              if (interactive && "onChange" in props) props.onChange(star);
            }}
          >
            {half ? (
              <>
                <defs>
                  <linearGradient id={`half-${star}`}>
                    <stop offset="50%" stopColor="#F59E0B" />
                    <stop offset="50%" stopColor="#374151" />
                  </linearGradient>
                </defs>
                <polygon
                  points="8,1 10.2,6.5 16,7 11.5,11.2 12.9,17 8,13.8 3.1,17 4.5,11.2 0,7 5.8,6.5"
                  fill={`url(#half-${star})`}
                />
              </>
            ) : (
              <polygon
                points="8,1 10.2,6.5 16,7 11.5,11.2 12.9,17 8,13.8 3.1,17 4.5,11.2 0,7 5.8,6.5"
                fill={filled ? "#F59E0B" : "#374151"}
              />
            )}
          </svg>
        );
      })}
    </span>
  );
}
