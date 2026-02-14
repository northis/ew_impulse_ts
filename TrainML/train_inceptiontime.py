import argparse
import csv
import os
from dataclasses import dataclass
import onnx

import numpy as np
import torch
from torch import nn
from torch.utils.data import DataLoader, TensorDataset, random_split


@dataclass
class TrainConfig:
    data_path: str
    output_dir: str
    epochs: int
    batch_size: int
    learning_rate: float
    val_split: float
    seed: int
    weight_decay: float
    patience: int
    dropout: float


class InceptionBlock(nn.Module):
    def __init__(self, in_channels: int, out_channels: int, kernel_sizes: list[int], dropout: float = 0.2):
        super().__init__()
        self.branches = nn.ModuleList(
            [
                nn.Conv1d(in_channels, out_channels, k, padding=k // 2)
                for k in kernel_sizes
            ]
        )
        self.bn = nn.BatchNorm1d(out_channels * len(kernel_sizes))
        self.relu = nn.ReLU()
        self.dropout = nn.Dropout(dropout)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        outputs = [branch(x) for branch in self.branches]
        x = torch.cat(outputs, dim=1)
        x = self.bn(x)
        x = self.relu(x)
        return self.dropout(x)


class InceptionTime(nn.Module):
    def __init__(self, in_channels: int, num_classes: int, dropout: float = 0.2):
        super().__init__()
        self.block1 = InceptionBlock(in_channels, 32, [9, 19, 39], dropout)
        self.block2 = InceptionBlock(96, 32, [9, 19, 39], dropout)
        self.block3 = InceptionBlock(96, 32, [9, 19, 39], dropout)
        self.pool = nn.AdaptiveAvgPool1d(1)
        self.fc = nn.Linear(96, num_classes)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        x = self.block1(x)
        x = self.block2(x)
        x = self.block3(x)
        x = self.pool(x).squeeze(-1)
        return self.fc(x)


def read_csv_dataset(path: str) -> tuple[np.ndarray, np.ndarray, int, list[str]]:
    with open(path, newline="", encoding="utf-8") as handle:
        reader = csv.reader(handle)
        header = next(reader)
        rows = list(reader)

    labels = np.array([int(row[0]) for row in rows], dtype=np.int64)
    features = np.array([[float(v) for v in row[2:]] for row in rows], dtype=np.float32)
    num_classes = int(labels.max()) + 1

    class_names: list[str] = [""] * num_classes
    for row in rows:
        idx = int(row[0])
        if not class_names[idx]:
            class_names[idx] = row[1]

    print(f"Dataset: {len(rows)} samples, {num_classes} classes, {features.shape[1]} features")
    for i, name in enumerate(class_names):
        count = int((labels == i).sum())
        print(f"  [{i}] {name}: {count} samples")

    return features, labels, num_classes, class_names


def build_dataloader(features: np.ndarray, labels: np.ndarray, batch_size: int, val_split: float, seed: int):
    feature_count = features.shape[1]
    bars_count = feature_count // 4
    tensor_x = torch.from_numpy(features.reshape(-1, 4, bars_count))
    tensor_y = torch.from_numpy(labels)

    dataset = TensorDataset(tensor_x, tensor_y)
    val_size = int(len(dataset) * val_split)
    train_size = len(dataset) - val_size
    generator = torch.Generator().manual_seed(seed)
    train_ds, val_ds = random_split(dataset, [train_size, val_size], generator=generator)

    train_loader = DataLoader(train_ds, batch_size=batch_size, shuffle=True)
    val_loader = DataLoader(val_ds, batch_size=batch_size, shuffle=False)
    return train_loader, val_loader, bars_count


def evaluate(model: nn.Module, loader: DataLoader, loss_fn: nn.Module) -> tuple[float, float]:
    model.eval()
    total_loss = 0.0
    correct = 0
    total = 0
    with torch.no_grad():
        for batch_x, batch_y in loader:
            logits = model(batch_x)
            total_loss += loss_fn(logits, batch_y).item() * batch_y.size(0)
            correct += (logits.argmax(dim=1) == batch_y).sum().item()
            total += batch_y.size(0)

    avg_loss = total_loss / max(1, total)
    accuracy = correct / max(1, total)
    return avg_loss, accuracy


def train(config: TrainConfig):
    torch.manual_seed(config.seed)
    features, labels, num_classes, class_names = read_csv_dataset(config.data_path)
    train_loader, val_loader, bars_count = build_dataloader(
        features, labels, config.batch_size, config.val_split, config.seed
    )

    model = InceptionTime(in_channels=4, num_classes=num_classes, dropout=config.dropout)
    optimizer = torch.optim.Adam(model.parameters(), lr=config.learning_rate, weight_decay=config.weight_decay)
    scheduler = torch.optim.lr_scheduler.CosineAnnealingLR(optimizer, T_max=config.epochs)
    loss_fn = nn.CrossEntropyLoss()

    best_val = float("inf")
    epochs_no_improve = 0
    os.makedirs(config.output_dir, exist_ok=True)
    model_path = os.path.join(config.output_dir, "inceptiontime.pt")

    for epoch in range(config.epochs):
        model.train()
        train_loss_sum = 0.0
        train_correct = 0
        train_total = 0
        for batch_x, batch_y in train_loader:
            optimizer.zero_grad()
            logits = model(batch_x)
            loss = loss_fn(logits, batch_y)
            loss.backward()
            optimizer.step()
            train_loss_sum += loss.item() * batch_y.size(0)
            train_correct += (logits.argmax(dim=1) == batch_y).sum().item()
            train_total += batch_y.size(0)

        scheduler.step()

        train_loss = train_loss_sum / max(1, train_total)
        train_acc = train_correct / max(1, train_total)
        val_loss, val_acc = evaluate(model, val_loader, loss_fn)

        lr_now = optimizer.param_groups[0]["lr"]
        print(
            f"Epoch {epoch + 1}/{config.epochs} - "
            f"train loss: {train_loss:.6f} - train acc: {train_acc:.4f} - "
            f"val loss: {val_loss:.6f} - val acc: {val_acc:.4f} - "
            f"lr: {lr_now:.2e}"
        )

        if val_loss < best_val:
            best_val = val_loss
            epochs_no_improve = 0
            torch.save(model.state_dict(), model_path)
        else:
            epochs_no_improve += 1
            if epochs_no_improve >= config.patience:
                print(f"Early stopping at epoch {epoch + 1} (no improvement for {config.patience} epochs)")
                break

    model.load_state_dict(torch.load(model_path, weights_only=True))
    model.eval()

    val_loss, val_acc = evaluate(model, val_loader, loss_fn)
    print(f"Best model - val loss: {val_loss:.6f} - val acc: {val_acc:.4f}")

    onnx_path = os.path.join(config.output_dir, "inceptiontime.onnx")
    dummy = torch.zeros(1, 4, bars_count, dtype=torch.float32)
    torch.onnx.export(
        model,
        dummy,
        onnx_path,
        input_names=["input"],
        output_names=["logits"],
        opset_version=18,
        dynamic_axes={"input": {0: "batch"}, "logits": {0: "batch"}},
        do_constant_folding=True,
    )

    onnx_model = onnx.load(onnx_path)
    onnx.save_model(
        onnx_model,
        onnx_path,
        save_as_external_data=False
    )
    print(f"Saved ONNX model (a single file) to {onnx_path}")


def parse_args() -> TrainConfig:
    parser = argparse.ArgumentParser()
    parser.add_argument("--data", required=True, help="Path to dataset CSV")
    parser.add_argument("--out", default="model", help="Output directory")
    parser.add_argument("--epochs", type=int, default=10)
    parser.add_argument("--batch", type=int, default=32)
    parser.add_argument("--lr", type=float, default=1e-3)
    parser.add_argument("--val", type=float, default=0.2)
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--wd", type=float, default=1e-4, help="Weight decay")
    parser.add_argument("--patience", type=int, default=15, help="Early stopping patience")
    parser.add_argument("--dropout", type=float, default=0.2, help="Dropout rate")
    args = parser.parse_args()

    return TrainConfig(
        data_path=args.data,
        output_dir=args.out,
        epochs=args.epochs,
        batch_size=args.batch,
        learning_rate=args.lr,
        val_split=args.val,
        seed=args.seed,
        weight_decay=args.wd,
        patience=args.patience,
        dropout=args.dropout,
    )


if __name__ == "__main__":
    cfg = parse_args()
    train(cfg)
