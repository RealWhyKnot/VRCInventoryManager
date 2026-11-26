const formatters =
  typeof Intl === "undefined"
    ? null
    : {
        integers: new Intl.NumberFormat(),
        decimals: new Intl.NumberFormat(undefined, {
          maximumFractionDigits: 1,
        }),
        shortDate: new Intl.DateTimeFormat(undefined, {
          month: "short",
          day: "numeric",
        }),
        fullDate: new Intl.DateTimeFormat(undefined, {
          dateStyle: "medium",
          timeStyle: "short",
        }),
      };

const months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

export const numberText = (number: number) => formatters?.integers.format(number) ?? String(number).replace(/\B(?=(\d{3})+(?!\d))/g, ",");

export const decimalText = (number: number) => formatters?.decimals.format(number) ?? String(Math.round(number * 10) / 10);

export function dateText(date: Date, isFull = false): string {
  if (formatters) return (isFull ? formatters.fullDate : formatters.shortDate).format(date);
  const short = `${months[date.getUTCMonth()]} ${date.getUTCDate()}`;
  return isFull ? `${short}, ${date.getUTCFullYear()} · ${date.toISOString().slice(11, 16)} UTC` : short;
}

export function nonEmpty(value: string | null | undefined): string | undefined {
  if (value) return value;
  return undefined;
}
